internal sealed record DeviceRegistration(string Host, ushort Port, string Username, string Password, bool Enabled = true);
internal sealed record DeviceOptions(string DeviceIp, ushort DevicePort, string Username, string Password);
internal sealed record ChannelInfo(int Channel, string Name, string? Model, bool Online, bool PtzCapable, string? Codec = null);
internal sealed record DeviceSnapshot(DeviceSummary Device, IReadOnlyList<ChannelInfo> Channels);
internal sealed record RecordingSearchRequest(int Channel, DateTimeOffset Start, DateTimeOffset End);
internal sealed record RecordingSummary(string FileName, DateTimeOffset Start, DateTimeOffset End, long FileSize, int FileType, int StreamType, uint FileIndex);
internal sealed record PlaybackSegment(DateTimeOffset Start, DateTimeOffset End, string FileName, long FileSize, int FileType, int StreamType, uint FileIndex);
internal sealed record LiveStartRequest(Guid SessionId, int Channel, int StreamType = 2, string Profile = "native");
internal sealed record LiveSummary(string Stream, int StreamType, string Codec, bool Transcoded);
internal sealed record PlaybackStartRequest(Guid SessionId, long UserId, int Channel, DateTimeOffset Start, DateTimeOffset End, string Profile = "native");
internal sealed record PlaybackControlRequest(string Action, DateTimeOffset? Position = null, double? Speed = null);
internal sealed record PlaybackSummary(Guid Id, string Stream, string State, DateTimeOffset Start, DateTimeOffset End, DateTimeOffset CurrentTime, int Progress, double Speed, IReadOnlyList<PlaybackSegment> Segments, string Codec, bool Transcoded, string? Error = null);
internal sealed record PtzRequest(int Channel, string Command, uint Speed = 4, bool Stop = false);
internal sealed record PresetRequest(int Channel, uint Preset);
internal sealed record ExportRequest(Guid JobId, int Channel, DateTimeOffset Start, DateTimeOffset End, string OutputDirectory);
internal sealed record ExportSummary(string State, int Progress, string? Path = null, string? Error = null);
internal sealed record AlarmEvent(string Id, long DeviceId, int? Channel, string EventType, DateTimeOffset OccurredAt, bool Recovered, object Payload, string? ImageBase64 = null);
internal sealed record AlarmPage(IReadOnlyList<AlarmEvent> Items, string NextCursor);
internal sealed record AckRequest(string Cursor);

internal sealed class AdapterException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

internal static class Validate
{
    public static void Channel(int channel)
    {
        if (channel is < 1 or > 65535) throw new ArgumentException("设备通道号超出范围。");
    }
    public static void Range(int channel, DateTimeOffset start, DateTimeOffset end)
    {
        Channel(channel);
        if (start.Year < 2000 || end <= start || end - start > TimeSpan.FromHours(24))
            throw new ArgumentException("录像范围必须为正且不能超过 24 小时。");
    }
    public static void Session(Guid id, string profile)
    {
        if (id == Guid.Empty) throw new ArgumentException("必须提供平台生成的会话标识。");
        if (profile is not ("native" or "browser")) throw new ArgumentException("输出配置无效。");
    }
}
