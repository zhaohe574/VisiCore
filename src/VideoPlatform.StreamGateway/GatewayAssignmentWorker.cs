using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VideoPlatform.Core;

namespace VideoPlatform.StreamGateway;

public sealed class GatewayAssignmentWorker(
    GatewayControlPlaneClient controlPlaneClient,
    IMediaMtxClient mediaMtxClient,
    DpapiGatewayCredentialResolver credentialResolver,
    GatewayPathRegistry pathRegistry,
    GatewayOptions options,
    LiveTranscodeOptions liveTranscodeOptions,
    ILiveTranscodeRelayManager liveTranscodeRelayManager,
    ILogger<GatewayAssignmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SynchronizeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "流网关同步设备路由失败，下个周期继续重试。");
            }
            await Task.Delay(TimeSpan.FromSeconds(options.AssignmentRefreshSeconds), stoppingToken);
        }
    }

    private async Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        var assignments = await controlPlaneClient.GetAssignmentsAsync(cancellationToken);
        var desired = new Dictionary<string, DesiredMediaPath>(StringComparer.Ordinal);
        var liveTranscodeRoutes = new Dictionary<string, LiveTranscodeRoute>(StringComparer.Ordinal);
        var completeSnapshot = true;
        foreach (var assignment in assignments)
        {
            try
            {
                AddAssignmentPaths(assignment, desired, liveTranscodeRoutes);
            }
            catch (Exception exception)
            {
                completeSnapshot = false;
                logger.LogError(
                    "录像机 {RecorderId} 的媒体路径生成失败，失败类别 {FailureKind}。",
                    assignment.RecorderId,
                    exception.GetType().Name);
            }
        }

        var pending = desired.Values
            .Where(item => !pathRegistry.IsCurrent(item.PathName, item.Fingerprint))
            .ToList();
        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (item, token) =>
            {
                if (item.Kind == DesiredMediaPathKind.Publisher)
                {
                    await mediaMtxClient.ApplyPublisherPathAsync(item.PathName, token);
                }
                else
                {
                    await mediaMtxClient.ApplyPullPathAsync(item.PathName, item.SourceUri!, token);
                }
            });

        await liveTranscodeRelayManager.SynchronizeRoutesAsync(
            liveTranscodeRoutes.Values.ToList(),
            completeSnapshot,
            cancellationToken);
        foreach (var item in desired.Values)
        {
            pathRegistry.MarkCurrent(item.PathName, item.Fingerprint, item.ClientReadable);
        }

        if (!completeSnapshot)
        {
            logger.LogWarning("本轮设备路由快照不完整，保留既有 MediaMTX 路径并跳过删除。");
            return;
        }

        var staleNames = pathRegistry.SnapshotNames().Except(desired.Keys, StringComparer.Ordinal).ToList();
        await Parallel.ForEachAsync(
            staleNames,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (pathName, token) =>
            {
                await mediaMtxClient.RemovePathAsync(pathName, token);
                pathRegistry.Remove(pathName);
            });

        logger.LogInformation(
            "流网关路由同步完成：目标 {DesiredCount}，更新 {UpdatedCount}，移除 {RemovedCount}。",
            desired.Count,
            pending.Count,
            staleNames.Count);
    }

    private void AddAssignmentPaths(
        WorkerRecorderAssignment assignment,
        IDictionary<string, DesiredMediaPath> desired,
        IDictionary<string, LiveTranscodeRoute> liveTranscodeRoutes)
    {
        var endpoint = assignment.Endpoints.SingleOrDefault(item => item.Protocol.Equals("Rtsp", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("录像机分配缺少 RTSP 端点。");
        var protectedCredential = assignment.Credentials.SingleOrDefault(item => item.Name == endpoint.CredentialReference)
            ?? throw new InvalidOperationException("录像机分配缺少 RTSP 凭据。");
        var credential = credentialResolver.Resolve(protectedCredential);

        foreach (var camera in assignment.Cameras)
        {
            using var streamMap = JsonDocument.Parse(camera.StreamingChannelMap);
            if (!streamMap.RootElement.TryGetProperty("main", out var mainMapping) ||
                !streamMap.RootElement.TryGetProperty("sub", out var subMapping))
            {
                throw new InvalidOperationException("摄像头码流映射不完整。");
            }

            var subSourceUri = BuildSourceUri(endpoint, credential, subMapping);
            var subPath = $"live/{camera.CameraId:N}/sub";
            desired[subPath] = DesiredMediaPath.Pull(subPath, subSourceUri, clientReadable: true);

            var mainSourceUri = BuildSourceUri(endpoint, credential, mainMapping);
            var publicMainPath = LiveTranscodePath.BuildPublicMain(camera.CameraId);
            if (!liveTranscodeOptions.IsEnabledFor(assignment.RecorderId))
            {
                desired[publicMainPath] = DesiredMediaPath.Pull(publicMainPath, mainSourceUri, clientReadable: true);
                continue;
            }

            var internalSourcePath = LiveTranscodePath.BuildInternalSource(camera.CameraId);
            desired[internalSourcePath] = DesiredMediaPath.Pull(
                internalSourcePath,
                mainSourceUri,
                clientReadable: false);
            desired[publicMainPath] = DesiredMediaPath.Publisher(publicMainPath, clientReadable: true);

            var routeFingerprint = Fingerprint($"{mainSourceUri.AbsoluteUri}\n{internalSourcePath}\n{publicMainPath}");
            liveTranscodeRoutes[publicMainPath] = new LiveTranscodeRoute(
                publicMainPath,
                internalSourcePath,
                liveTranscodeOptions.BuildMediaMtxUri(internalSourcePath),
                liveTranscodeOptions.BuildMediaMtxUri(publicMainPath),
                routeFingerprint);
        }
    }

    private static Uri BuildSourceUri(
        WorkerRecorderEndpoint endpoint,
        NetworkCredential credential,
        JsonElement mapping)
    {
        if (!RecorderEndpointHostPolicy.IsValidHost(endpoint.Host) || endpoint.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("录像机 RTSP 端点主机或端口无效。");
        }
        UriBuilder builder;
        var endpointScheme = endpoint.UseTls ? "rtsps" : "rtsp";
        if (mapping.ValueKind == JsonValueKind.Number && mapping.TryGetInt32(out var channelId) && channelId > 0)
        {
            builder = new UriBuilder(endpointScheme, endpoint.Host, endpoint.Port, $"Streaming/Channels/{channelId}");
        }
        else if (mapping.ValueKind == JsonValueKind.String)
        {
            var value = mapping.GetString() ?? string.Empty;
            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            {
                if (!absolute.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
                    !absolute.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("绝对码流地址必须使用 RTSP 或 RTSPS。");
                }
                if (!absolute.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("绝对码流地址不能越过录像机 RTSP 端点。");
                }
                if (absolute.Port != endpoint.Port)
                {
                    throw new InvalidOperationException("绝对码流地址不能改变录像机 RTSP 端口。");
                }
                if (!absolute.Scheme.Equals(endpointScheme, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("绝对码流地址的 RTSP 安全模式与设备端点不一致。");
                }
                builder = new UriBuilder(absolute);
            }
            else if (value.StartsWith("/", StringComparison.Ordinal))
            {
                builder = BuildEndpointUri(endpointScheme, endpoint.Host, endpoint.Port, value);
            }
            else
            {
                throw new InvalidOperationException("字符串码流映射必须是绝对 RTSP 地址或以斜杠开头的路径。");
            }
        }
        else
        {
            throw new InvalidOperationException("码流映射只允许正整数或字符串。");
        }

        builder.UserName = credential.UserName;
        builder.Password = credential.Password;
        return builder.Uri;
    }

    private static UriBuilder BuildEndpointUri(string scheme, string host, int port, string pathAndQuery)
    {
        var queryIndex = pathAndQuery.IndexOf('?', StringComparison.Ordinal);
        return queryIndex < 0
            ? new UriBuilder(scheme, host, port, pathAndQuery)
            : new UriBuilder(scheme, host, port, pathAndQuery[..queryIndex])
            {
                Query = pathAndQuery[(queryIndex + 1)..]
            };
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public enum DesiredMediaPathKind
{
    Pull = 0,
    Publisher = 1
}

public sealed record DesiredMediaPath(
    string PathName,
    Uri? SourceUri,
    string Fingerprint,
    DesiredMediaPathKind Kind,
    bool ClientReadable)
{
    public static DesiredMediaPath Pull(string pathName, Uri sourceUri, bool clientReadable) =>
        new(pathName, sourceUri, ComputeFingerprint($"pull\n{sourceUri.AbsoluteUri}"), DesiredMediaPathKind.Pull, clientReadable);

    public static DesiredMediaPath Publisher(string pathName, bool clientReadable) =>
        new(pathName, null, ComputeFingerprint($"publisher-v1\n{pathName}"), DesiredMediaPathKind.Publisher, clientReadable);

    private static string ComputeFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
