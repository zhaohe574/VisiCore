using System.Diagnostics;
using System.Runtime.InteropServices;
using Native = HCNetSDKInterop;

internal sealed partial class HikvisionDevice
{
    public IReadOnlyList<RecordingSummary> SearchRecordings(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        lock (_searchGate)
        {
            lock (_sdkGate) EnsureConnected();
            // NET_DVR_FILECOND_V50 的固定大小为 424 字节。
            var condition = Marshal.AllocHGlobal(424);
            try
            {
                Zero(condition, 424);
                WriteUInt32(condition, 0, 72);
                WriteUInt32(condition, 36, channel);
                WriteSearchTime(condition, 72, start);
                WriteSearchTime(condition, 84, end);
                Marshal.WriteByte(condition, 96, 0); // 按通道查询
                Marshal.WriteByte(condition, 97, 0); // 不抽帧
                Marshal.WriteByte(condition, 98, 0); // 普通查询
                Marshal.WriteByte(condition, 99, 0); // 主码流
                WriteUInt32(condition, 100, 0xff); // 厂商定义的全部录像类型，不能写成 UINT_MAX。
                Marshal.WriteByte(condition, 108, 0xff); // 不限制锁定状态
                WriteUInt32(condition, 168, 15000); // SDK 查询等待上限（毫秒）
                var findHandle = Native.NET_DVR_FindFile_V50(_controlUserId, condition);
                if (findHandle < 0)
                {
                    if (Native.NET_DVR_GetLastError() == 23)
                        return SearchRecordingsV40Locked(channel, start, end);
                    throw SdkError("录像查询建立失败");
                }
                try
                {
                    var results = new List<RecordingSummary>();
                    var data = Marshal.AllocHGlobal(572);
                    try
                    {
                        var searchStarted = Stopwatch.GetTimestamp();
                        while (true)
                        {
                            if (Stopwatch.GetElapsedTime(searchStarted) >= TimeSpan.FromMinutes(2))
                                throw new TimeoutException("录像查询超时");
                            Zero(data, 572);
                            var state = Native.NET_DVR_FindNextFile_V50(findHandle, data);
                            if (state == 1000)
                            {
                                results.Add(ParseRecording(data));
                                continue;
                            }
                            if (state == 1002)
                            {
                                Thread.Sleep(50);
                                continue;
                            }
                            if (state is 1001 or 1003)
                                return results;
                            throw SdkError($"录像查询失败，SDK 状态：{state}");
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(data);
                    }
                }
                finally
                {
                    Native.NET_DVR_FindClose_V30(findHandle);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(condition);
            }
        }
    }

    private IReadOnlyList<RecordingSummary> SearchRecordingsV40Locked(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        var condition = Marshal.AllocHGlobal(160);
        try
        {
            Zero(condition, 160);
            WriteUInt32(condition, 0, channel);
            WriteUInt32(condition, 4, 0xff);
            WriteUInt32(condition, 8, 0xff);
            WriteLegacyTime(condition, 48, start);
            WriteLegacyTime(condition, 72, end);
            Marshal.WriteByte(condition, 97, 0); // 按通道查询
            Marshal.WriteByte(condition, 98, 0); // 普通查询
            Marshal.WriteByte(condition, 128, 0); // 主码流，兼容旧型号设备
            var findHandle = Native.NET_DVR_FindFile_V40(_controlUserId, condition);
            if (findHandle < 0)
            {
                var error = Native.NET_DVR_GetLastError();
                if (error is 11 or 23)
                    return SearchRecordingsV30Locked(channel, start, end);
                throw SdkError("录像查询建立失败");
            }
            try
            {
                var results = new List<RecordingSummary>();
                var data = Marshal.AllocHGlobal(324);
                try
                {
                    var searchStarted = Stopwatch.GetTimestamp();
                    while (true)
                    {
                        if (Stopwatch.GetElapsedTime(searchStarted) >= TimeSpan.FromMinutes(2))
                            throw new TimeoutException("录像查询超时");
                        Zero(data, 324);
                        var state = Native.NET_DVR_FindNextFile_V40(findHandle, data);
                        if (state == 1000)
                        {
                            results.Add(ParseRecordingV40(data));
                            continue;
                        }
                        if (state == 1002)
                        {
                            Thread.Sleep(50);
                            continue;
                        }
                        if (state is 1001 or 1003)
                            return results;
                        throw SdkError($"录像查询失败，SDK 状态：{state}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
            }
            finally
            {
                Native.NET_DVR_FindClose_V30(findHandle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(condition);
        }
    }

    private IReadOnlyList<RecordingSummary> SearchRecordingsV30Locked(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        var condition = Marshal.AllocHGlobal(96);
        try
        {
            Zero(condition, 96);
            WriteUInt32(condition, 0, channel);
            WriteUInt32(condition, 4, 0xff);
            WriteUInt32(condition, 8, 0xff);
            WriteLegacyTime(condition, 48, start);
            WriteLegacyTime(condition, 72, end);
            var findHandle = Native.NET_DVR_FindFile_V30(_controlUserId, condition);
            if (findHandle < 0)
                throw SdkError("录像查询建立失败");
            try
            {
                var results = new List<RecordingSummary>();
                var data = Marshal.AllocHGlobal(188);
                try
                {
                    var searchStarted = Stopwatch.GetTimestamp();
                    while (true)
                    {
                        if (Stopwatch.GetElapsedTime(searchStarted) >= TimeSpan.FromMinutes(2))
                            throw new TimeoutException("录像查询超时");
                        Zero(data, 188);
                        var state = Native.NET_DVR_FindNextFile_V30(findHandle, data);
                        if (state == 1000)
                        {
                            results.Add(ParseRecordingV30(data));
                            continue;
                        }
                        if (state == 1002)
                        {
                            Thread.Sleep(50);
                            continue;
                        }
                        if (state is 1001 or 1003)
                            return results;
                        throw SdkError($"录像查询失败，SDK 状态：{state}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(data);
                }
            }
            finally
            {
                Native.NET_DVR_FindClose_V30(findHandle);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(condition);
        }
    }

    private static void WriteSearchTime(IntPtr pointer, int offset, DateTimeOffset value)
    {
        var local = value.ToOffset(TimeSpan.FromHours(8));
        Marshal.WriteInt16(pointer, offset, (short)local.Year);
        Marshal.WriteByte(pointer, offset + 2, (byte)local.Month);
        Marshal.WriteByte(pointer, offset + 3, (byte)local.Day);
        Marshal.WriteByte(pointer, offset + 4, (byte)local.Hour);
        Marshal.WriteByte(pointer, offset + 5, (byte)local.Minute);
        Marshal.WriteByte(pointer, offset + 6, (byte)local.Second);
        Marshal.WriteByte(pointer, offset + 7, 0); // 使用设备本地时间
        Marshal.WriteInt16(pointer, offset + 8, (short)local.Millisecond);
        Marshal.WriteByte(pointer, offset + 10, 8);
        Marshal.WriteByte(pointer, offset + 11, 0);
    }

    private static void WriteLegacyTime(IntPtr pointer, int offset, DateTimeOffset value)
    {
        var local = value.ToOffset(TimeSpan.FromHours(8));
        Marshal.WriteInt32(pointer, offset, local.Year);
        Marshal.WriteInt32(pointer, offset + 4, local.Month);
        Marshal.WriteInt32(pointer, offset + 8, local.Day);
        Marshal.WriteInt32(pointer, offset + 12, local.Hour);
        Marshal.WriteInt32(pointer, offset + 16, local.Minute);
        Marshal.WriteInt32(pointer, offset + 20, local.Second);
    }

    private static RecordingSummary ParseRecording(IntPtr pointer)
    {
        var fileName = Decode(pointer, 0, 100);
        var start = ReadSearchTime(pointer, 100);
        var end = ReadSearchTime(pointer, 112);
        var fileSize = unchecked((long)(uint)Marshal.ReadInt32(pointer, 272));
        var high = unchecked((uint)Marshal.ReadInt32(pointer, 316));
        var low = unchecked((uint)Marshal.ReadInt32(pointer, 320));
        if (Marshal.ReadByte(pointer, 324) == 1)
            fileSize = (long)(((ulong)high << 32) | low);
        return new RecordingSummary(fileName, start, end, fileSize, Marshal.ReadByte(pointer, 277), Marshal.ReadByte(pointer, 279), unchecked((uint)Marshal.ReadInt32(pointer, 280)));
    }

    private static DateTimeOffset ReadSearchTime(IntPtr pointer, int offset)
    {
        var year = Marshal.ReadInt16(pointer, offset);
        var month = Marshal.ReadByte(pointer, offset + 2);
        var day = Marshal.ReadByte(pointer, offset + 3);
        var hour = Marshal.ReadByte(pointer, offset + 4);
        var minute = Marshal.ReadByte(pointer, offset + 5);
        var second = Marshal.ReadByte(pointer, offset + 6);
        var localOrUtc = Marshal.ReadByte(pointer, offset + 7) != 0;
        var milliseconds = Marshal.ReadInt16(pointer, offset + 8);
        var hours = localOrUtc ? (sbyte)Marshal.ReadByte(pointer, offset + 10) : (sbyte)8;
        var minutes = localOrUtc ? (sbyte)Marshal.ReadByte(pointer, offset + 11) : (sbyte)0;
        try { return new DateTimeOffset(year, month, day, hour, minute, second, milliseconds, new TimeSpan(hours, minutes, 0)); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MinValue; }
    }

    private static RecordingSummary ParseRecordingV40(IntPtr pointer)
    {
        var fileName = Decode(pointer, 0, 100);
        var start = ReadLegacyTime(pointer, 100);
        var end = ReadLegacyTime(pointer, 124);
        var fileSize = unchecked((long)(uint)Marshal.ReadInt32(pointer, 148));
        return new RecordingSummary(fileName, start, end, fileSize, Marshal.ReadByte(pointer, 185), Marshal.ReadByte(pointer, 192), unchecked((uint)Marshal.ReadInt32(pointer, 188)));
    }

    private static RecordingSummary ParseRecordingV30(IntPtr pointer)
    {
        var fileName = Decode(pointer, 0, 100);
        var start = ReadLegacyTime(pointer, 100);
        var end = ReadLegacyTime(pointer, 124);
        var fileSize = unchecked((long)(uint)Marshal.ReadInt32(pointer, 148));
        return new RecordingSummary(fileName, start, end, fileSize, Marshal.ReadByte(pointer, 185), 0, 0);
    }

    private static DateTimeOffset ReadLegacyTime(IntPtr pointer, int offset)
    {
        var year = Marshal.ReadInt32(pointer, offset);
        var month = Marshal.ReadInt32(pointer, offset + 4);
        var day = Marshal.ReadInt32(pointer, offset + 8);
        var hour = Marshal.ReadInt32(pointer, offset + 12);
        var minute = Marshal.ReadInt32(pointer, offset + 16);
        var second = Marshal.ReadInt32(pointer, offset + 20);
        try { return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(8)); }
        catch (ArgumentOutOfRangeException) { return DateTimeOffset.MinValue; }
    }

}
