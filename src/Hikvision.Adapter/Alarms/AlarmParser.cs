using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

internal sealed record AdapterAlarmEvent(DateTimeOffset ReceivedAt, int Command, byte[] Payload, AlarmEventDetails? Details)
{
    public string EventId { get; } = Guid.NewGuid().ToString("N");
}
internal sealed record AlarmEventDetails(
    string EventType,
    uint AlarmType,
    IReadOnlyList<int> Channels,
    bool? IsRecovery,
    DateTimeOffset? AlarmTime,
    byte[]? Image,
    string? ImageUrl);

internal static class AlarmParser
{
    private const int CommAlarm = 0x1100;
    private const int V30ChannelOffset = 168;
    private const int V30ChannelCount = 64;
    private const int MaxImageBytes = 8 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<uint, string> EventNames = new Dictionary<uint, string>
    {
        [0] = "alarm.input",
        [1] = "alarm.disk",
        [2] = "alarm.video_loss",
        [3] = "alarm.motion",
        [4] = "alarm.disk_unformatted",
        [5] = "alarm.disk_write_error",
        [6] = "alarm.tamper",
        [7] = "alarm.format_mismatch",
        [8] = "alarm.illegal_access",
        [9] = "alarm.video_exception",
        [10] = "alarm.recording_exception",
        [11] = "alarm.ip_conflict",
        [12] = "alarm.network",
        [13] = "alarm.stream_mismatch",
        [14] = "alarm.network_error",
        [15] = "alarm.smart",
        [16] = "alarm.standby_exception",
        [17] = "alarm.recording_space_low",
        [18] = "alarm.recording_failure",
        [19] = "alarm.video_loss",
        [20] = "alarm.recording",
        [21] = "alarm.recording_stopped",
        [22] = "alarm.algorithm_exception",
        [23] = "alarm.thermal",
        [24] = "alarm.disk_exception",
        [25] = "alarm.network",
        [26] = "alarm.picture_upload",
        [27] = "alarm.poc_exception",
        [28] = "alarm.video_exception",
        [30] = "alarm.sd_missing",
        [31] = "alarm.voltage",
        [32] = "alarm.ptz_exception",
        [33] = "alarm.log_exception",
        [34] = "alarm.abnormal_reboot"
    };

    public static AlarmEventDetails? Parse(int command, IntPtr alarmInfo, uint bufferLength)
    {
        if (command != CommAlarm || alarmInfo == IntPtr.Zero || bufferLength < 4)
            return null;
        var length = (int)Math.Min(bufferLength, 4096u);
        var bytes = new byte[length];
        Marshal.Copy(alarmInfo, bytes, 0, length);
        var alarmType = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (length <= 200 && TryReadTime(bytes, 4, out var v40Time))
            return ParseV40(alarmInfo, bytes, alarmType, v40Time);
        return ParseV30(bytes, alarmType);
    }

    private static AlarmEventDetails ParseV30(byte[] bytes, uint alarmType)
    {
        var channels = new List<int>();
        if (bytes.Length >= V30ChannelOffset + V30ChannelCount)
            for (var i = 0; i < V30ChannelCount; i++)
                if (bytes[V30ChannelOffset + i] != 0)
                    channels.Add(i + 1);
        return new AlarmEventDetails(
            EventNames.TryGetValue(alarmType, out var name) ? name : $"alarm.sdk_{alarmType}",
            alarmType,
            channels,
            IsChannelAlarm(alarmType) ? channels.Count == 0 : null,
            null,
            null,
            null);
    }

    private static AlarmEventDetails ParseV40(IntPtr pointer, byte[] bytes, uint alarmType, DateTimeOffset alarmTime)
    {
        var unionOffset = bytes.Length >= 160 ? 16 : 12;
        var channelCount = bytes.Length >= unionOffset + 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(unionOffset, 4)) : 0;
        var channelPointerOffset = unionOffset == 16 ? 152 : 144;
        var channels = bytes.Length >= channelPointerOffset + IntPtr.Size
            ? ReadV40Channels(pointer, channelPointerOffset, channelCount)
            : Array.Empty<int>();
        byte[]? image = null;
        string? imageUrl = null;
        if (IsImageAlarm(alarmType))
        {
            var imageLength = bytes.Length >= unionOffset + 8 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(unionOffset + 4, 4)) : 0;
            var byUrl = bytes.Length > unionOffset + 8 && bytes[unionOffset + 8] != 0;
            var dataOffset = unionOffset + 16;
            if (imageLength > 0 && imageLength <= MaxImageBytes && bytes.Length >= dataOffset + IntPtr.Size)
            {
                var data = Marshal.ReadIntPtr(pointer, dataOffset);
                if (data != IntPtr.Zero)
                {
                    if (byUrl)
                        imageUrl = ReadAnsi(data, 4096);
                    else
                    {
                        image = new byte[imageLength];
                        Marshal.Copy(data, image, 0, image.Length);
                    }
                }
            }
        }
        return new AlarmEventDetails(
            EventNames.TryGetValue(alarmType, out var name) ? name : $"alarm.sdk_{alarmType}",
            alarmType,
            channels,
            null,
            alarmTime,
            image,
            imageUrl);
    }

    private static IReadOnlyList<int> ReadV40Channels(IntPtr pointer, int dataOffset, uint count)
    {
        if (count == 0 || count > 512 || pointer == IntPtr.Zero)
            return Array.Empty<int>();
        var data = Marshal.ReadIntPtr(pointer, dataOffset);
        if (data == IntPtr.Zero)
            return Array.Empty<int>();
        var channels = new List<int>((int)Math.Min(count, 512));
        for (var i = 0; i < count; i++)
        {
            var channel = Marshal.ReadInt32(data, checked(i * sizeof(int)));
            if (channel > 0 && channel <= 65535)
                channels.Add(channel);
        }
        return channels;
    }

    private static bool TryReadTime(byte[] bytes, int offset, out DateTimeOffset value)
    {
        value = default;
        if (bytes.Length < offset + 8)
            return false;
        var year = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
        var month = bytes[offset + 2];
        var day = bytes[offset + 3];
        var hour = bytes[offset + 4];
        var minute = bytes[offset + 5];
        var second = bytes[offset + 6];
        if (year is < 2000 or > 2200 || month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59)
            return false;
        try
        {
            value = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(8));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string? ReadAnsi(IntPtr pointer, int maxLength)
    {
        var bytes = new byte[maxLength];
        var length = 0;
        while (length < bytes.Length && (bytes[length] = Marshal.ReadByte(pointer, length)) != 0)
            length++;
        return length == 0 ? null : Encoding.ASCII.GetString(bytes, 0, length);
    }

    private static bool IsChannelAlarm(uint alarmType) => alarmType is 2 or 3 or 6 or 9 or 10 or 14 or 19 or 28;
    private static bool IsImageAlarm(uint alarmType) => alarmType is 2 or 3 or 6 or 9 or 10 or 13 or 28;

    public static void SelfTest()
    {
        var bytes = new byte[268];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 3);
        bytes[V30ChannelOffset + 7] = 1;
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            var details = Parse(CommAlarm, pointer, (uint)bytes.Length);
            if (details is null || details.EventType != "alarm.motion" || details.Channels.Count != 1 || details.Channels[0] != 8 || details.IsRecovery != false)
                throw new InvalidOperationException("V30 报警解析结果不符合预期");
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        var image = new byte[] { 0xff, 0xd8, 0xff, 0xd9 };
        var v40 = new byte[160];
        BinaryPrimitives.WriteUInt32LittleEndian(v40, 3);
        BinaryPrimitives.WriteUInt16LittleEndian(v40.AsSpan(4), 2026);
        v40[6] = 9; v40[7] = 4; v40[8] = 12; v40[9] = 30; v40[10] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(v40.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(v40.AsSpan(20), (uint)image.Length);
        var imagePointer = Marshal.AllocHGlobal(image.Length);
        var channelPointer = Marshal.AllocHGlobal(sizeof(int));
        var v40Pointer = Marshal.AllocHGlobal(v40.Length);
        try
        {
            Marshal.Copy(image, 0, imagePointer, image.Length);
            Marshal.WriteInt32(channelPointer, 0, 8);
            Marshal.WriteIntPtr(v40Pointer, 32, imagePointer);
            Marshal.WriteIntPtr(v40Pointer, 152, channelPointer);
            Marshal.Copy(v40, 0, v40Pointer, v40.Length);
            // 指针字段在复制结构后写入，避免按平台字节序手工编码地址。
            Marshal.WriteIntPtr(v40Pointer, 32, imagePointer);
            Marshal.WriteIntPtr(v40Pointer, 152, channelPointer);
            var details = Parse(CommAlarm, v40Pointer, (uint)v40.Length);
            if (details is null || details.AlarmTime?.Year != 2026 || details.Channels.Count != 1 || details.Channels[0] != 8 || details.Image?.Length != image.Length)
                throw new InvalidOperationException("V40 报警解析结果不符合预期");
        }
        finally
        {
            Marshal.FreeHGlobal(v40Pointer);
            Marshal.FreeHGlobal(channelPointer);
            Marshal.FreeHGlobal(imagePointer);
        }
    }
}
