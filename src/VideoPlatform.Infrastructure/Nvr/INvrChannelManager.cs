using VideoPlatform.Contracts;

namespace VideoPlatform.Infrastructure.Nvr;

public interface INvrChannelManager
{
    Task<IReadOnlyList<NvrAvailableChannelDto>> GetAvailableChannelsAsync(CancellationToken ct = default);
    Task<int> AddIpcChannelAsync(
        long nvrDeviceId,
        int? channelNumber,
        string ipcHost,
        int ipcPort,
        string ipcUsername,
        string ipcPassword,
        string protocolType,
        string channelName,
        CancellationToken ct = default);
    Task UpdateIpcChannelAsync(
        long nvrDeviceId,
        int channelNumber,
        string? ipcHost,
        int? ipcPort,
        string? ipcUsername,
        string? ipcPassword,
        string? protocolType,
        string? channelName,
        CancellationToken ct = default);
}

