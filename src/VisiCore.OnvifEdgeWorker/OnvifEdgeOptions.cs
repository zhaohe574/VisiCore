namespace VisiCore.OnvifEdgeWorker;

public sealed class OnvifEdgeOptions
{
    public string? CenterBaseUri { get; init; }
    public string? AccessToken { get; init; }
    public bool AllowInsecureCenterHttpForDevelopment { get; init; }
    public string[] TrustedDevelopmentHttpHosts { get; init; } = [];
    public int AssignmentRefreshSeconds { get; init; } = 30;
    public int CommandPollMilliseconds { get; init; } = 1000;
    public int OperationStatusRefreshSeconds { get; init; } = 30;
    public OnvifPtzOptions Ptz { get; init; } = new();
    public OnvifProfileGOptions ProfileG { get; init; } = new();
    public OnvifPlaybackRelayOptions PlaybackRelay { get; init; } = new();

    public Uri ValidateAndGetCenterBaseUri()
    {
        if (string.IsNullOrWhiteSpace(CenterBaseUri) ||
            !Uri.TryCreate(CenterBaseUri, UriKind.Absolute, out var centerBaseUri) ||
            (!centerBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(AllowInsecureCenterHttpForDevelopment &&
               centerBaseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
               (centerBaseUri.IsLoopback || IsTrustedDevelopmentHttpHost(centerBaseUri.Host)))) ||
            !HasValidAccessToken() ||
            AssignmentRefreshSeconds is < 5 or > 300 ||
            CommandPollMilliseconds is < 250 or > 10_000 ||
            OperationStatusRefreshSeconds is < 10 or > 300)
        {
            throw new InvalidOperationException("ONVIF 边缘 Worker 的中心地址、令牌或轮询参数无效。 ");
        }
        Ptz.Validate();
        return new Uri(centerBaseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    private bool IsTrustedDevelopmentHttpHost(string host) =>
        TrustedDevelopmentHttpHosts.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            string.Equals(value.Trim(), host, StringComparison.OrdinalIgnoreCase));

    private bool HasValidAccessToken() =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        AccessToken.Length >= (AllowInsecureCenterHttpForDevelopment ? 24 : 32);
}

public sealed class OnvifPtzOptions
{
    public bool Enabled { get; init; }
    public bool AllowUntrustedCertificate { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxPulseMilliseconds { get; init; } = 2000;

    public void Validate()
    {
        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromMinutes(1) ||
            MaxPulseMilliseconds is < 250 or > 5000)
        {
            throw new InvalidOperationException("ONVIF PTZ 的超时或最大脉冲时长无效。 ");
        }
    }

    public void ValidateEnabled()
    {
        Validate();
        if (!Enabled)
        {
            throw new NotSupportedException("ONVIF PTZ 写控制未启用。 ");
        }
    }
}

public sealed class OnvifProfileGOptions
{
    public bool Enabled { get; init; }
    public bool AllowUntrustedCertificate { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxSearchResults { get; init; } = 200;
    public int MaxSearchPolls { get; init; } = 4;

    public void ValidateEnabled()
    {
        if (!Enabled || RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromMinutes(1) ||
            MaxSearchResults is < 1 or > 200 || MaxSearchPolls is < 1 or > 8)
        {
            throw new NotSupportedException("ONVIF Profile G 检索未启用或配置无效。 ");
        }
    }
}

public sealed class OnvifPlaybackRelayOptions
{
    public bool Enabled { get; init; }
    public string? FfmpegExecutablePath { get; init; }
    public string? PublisherBaseUri { get; init; }
    public string? PublisherUsername { get; init; }
    public string? PublisherPassword { get; init; }
    public OnvifPlaybackRelayVideoMode VideoMode { get; init; } = OnvifPlaybackRelayVideoMode.H264Compatible;
    public int MaxConcurrentRelays { get; init; } = 2;
    public int MaxRelayLifetimeSeconds { get; init; } = 300;
    public int StartupProbeMilliseconds { get; init; } = 500;
    public int AuthorizationCheckSeconds { get; init; } = 5;
    public int ValidationCacheMinutes { get; init; } = 30;
    public IReadOnlyList<Guid> ValidatedRecorderIds { get; init; } = [];

    public void ValidateEnabled()
    {
        if (!Enabled ||
            string.IsNullOrWhiteSpace(FfmpegExecutablePath) ||
            !Path.IsPathFullyQualified(FfmpegExecutablePath) ||
            !File.Exists(FfmpegExecutablePath) ||
            string.IsNullOrWhiteSpace(PublisherBaseUri) ||
            !Uri.TryCreate(PublisherBaseUri, UriKind.Absolute, out var publisherBaseUri) ||
            !publisherBaseUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
            !publisherBaseUri.IsLoopback ||
            !string.IsNullOrEmpty(publisherBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(publisherBaseUri.Query) ||
            !string.IsNullOrEmpty(publisherBaseUri.Fragment) ||
            publisherBaseUri.Port is < 1 or > 65535 ||
            string.IsNullOrWhiteSpace(PublisherUsername) ||
            PublisherUsername.Length > 64 ||
            !PublisherUsername.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
            string.IsNullOrWhiteSpace(PublisherPassword) ||
            PublisherPassword.Length is < 32 or > 256 ||
            !Enum.IsDefined(VideoMode) ||
            MaxConcurrentRelays is < 1 or > 8 ||
            MaxRelayLifetimeSeconds is < 60 or > 900 ||
            StartupProbeMilliseconds is < 100 or > 5_000 ||
            AuthorizationCheckSeconds is < 2 or > 30 ||
            ValidationCacheMinutes is < 1 or > 720 ||
            ValidatedRecorderIds.Any(item => item == Guid.Empty))
        {
            throw new NotSupportedException("ONVIF 回放中继未启用或 FFmpeg、回环发布地址、并发数、时长、授权检查配置无效。 ");
        }
    }

    public bool IsRecorderExplicitlyValidated(Guid recorderId) =>
        recorderId != Guid.Empty && ValidatedRecorderIds.Contains(recorderId);

    public Uri BuildPublisherUri(Guid playbackSessionId)
    {
        ValidateEnabled();
        if (playbackSessionId == Guid.Empty || !Uri.TryCreate(PublisherBaseUri, UriKind.Absolute, out var publisherBaseUri))
        {
            throw new ArgumentOutOfRangeException(nameof(playbackSessionId));
        }

        var builder = new UriBuilder(publisherBaseUri)
        {
            Path = $"{publisherBaseUri.AbsolutePath.TrimEnd('/')}/playback/{playbackSessionId:N}",
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = PublisherUsername,
            Password = PublisherPassword
        };
        return builder.Uri;
    }
}

public enum OnvifPlaybackRelayVideoMode
{
    H264Compatible = 0,
    Copy = 1
}
