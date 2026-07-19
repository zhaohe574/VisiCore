using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace VisiCore.StreamGateway;

public sealed class GatewayOptions
{
    public string GatewayName { get; init; } = "default";
    public string? CenterBaseUri { get; init; }
    public string? CenterControlToken { get; init; }
    public string? DeviceWorkerAccessToken { get; init; }
    public string? CommandToken { get; init; }
    public bool AllowInsecureCenterHttpForDevelopment { get; init; }
    public int AssignmentRefreshSeconds { get; init; } = 30;
    public int SessionInspectionSeconds { get; init; } = 5;
    public int MaxReadersPerPath { get; init; } = 100;
    public string[] TrustedForwardedProxyAddresses { get; init; } = [];

    public Uri ValidateAndGetCenterBaseUri()
    {
        if (string.IsNullOrWhiteSpace(GatewayName) || GatewayName.Length > 64 ||
            string.IsNullOrWhiteSpace(CenterBaseUri) ||
            !Uri.TryCreate(CenterBaseUri, UriKind.Absolute, out var centerBaseUri) ||
            (!centerBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !(AllowInsecureCenterHttpForDevelopment && centerBaseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && centerBaseUri.IsLoopback)) ||
            string.IsNullOrWhiteSpace(CenterControlToken) || CenterControlToken.Length < 32 ||
            string.IsNullOrWhiteSpace(DeviceWorkerAccessToken) || DeviceWorkerAccessToken.Length < 32 ||
            string.IsNullOrWhiteSpace(CommandToken) || CommandToken.Length < 32 ||
            string.Equals(CenterControlToken, CommandToken, StringComparison.Ordinal) ||
            AssignmentRefreshSeconds is < 5 or > 300 || SessionInspectionSeconds is < 1 or > 30 ||
            MaxReadersPerPath is < 1 or > 1000 ||
            !TryGetTrustedForwardedProxyAddresses(out _, out _))
        {
            throw new InvalidOperationException("流网关名称、中心地址、独立令牌或运行参数无效。");
        }

        return new Uri(centerBaseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    public IReadOnlyList<IPAddress> GetTrustedForwardedProxyAddresses()
    {
        if (!TryGetTrustedForwardedProxyAddresses(out var addresses, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }
        return addresses;
    }

    private bool TryGetTrustedForwardedProxyAddresses(
        out IReadOnlyList<IPAddress> addresses,
        out string validationError)
    {
        var parsed = new List<IPAddress>();
        foreach (var value in TrustedForwardedProxyAddresses)
        {
            if (!IPAddress.TryParse(value, out var address) || address.IsIPv6Multicast || address.IsIPv6LinkLocal)
            {
                addresses = [];
                validationError = "受信任反向代理地址无效。";
                return false;
            }
            parsed.Add(address);
        }
        if (parsed.Distinct().Count() != parsed.Count)
        {
            addresses = [];
            validationError = "受信任反向代理地址不能重复。";
            return false;
        }

        addresses = parsed;
        validationError = string.Empty;
        return true;
    }

    public bool IsCommandTokenValid(string? presentedToken)
    {
        if (string.IsNullOrWhiteSpace(CommandToken) || string.IsNullOrWhiteSpace(presentedToken))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(CommandToken)),
            SHA256.HashData(Encoding.UTF8.GetBytes(presentedToken)));
    }
}

public sealed class MediaMtxOptions
{
    public string ApiBaseUri { get; init; } = "http://127.0.0.1:9997/";
    public string HlsBaseUri { get; init; } = "http://127.0.0.1:8888/";
    public string? PublisherUsername { get; init; }
    public string? PublisherPassword { get; init; }
    public string? HlsReaderUsername { get; init; }
    public string? HlsReaderPassword { get; init; }
    public int RequestTimeoutSeconds { get; init; } = 5;
    public int PlaylistStartupRetrySeconds { get; init; } = 15;
    public int PlaylistStartupRetryMilliseconds { get; init; } = 200;
    public string SourceOnDemandStartTimeout { get; init; } = "10s";
    public string SourceOnDemandCloseAfter { get; init; } = "15s";
    public string[] TrustedInternalHosts { get; init; } = [];
    public string[] TrustedAuthCallbackAddresses { get; init; } = [];
    public string[] TrustedHlsReaderAddresses { get; init; } = [];

    public Uri ValidateAndGetApiBaseUri()
    {
        if (!Uri.TryCreate(ApiBaseUri, UriKind.Absolute, out var apiBaseUri) ||
            !IsAllowedInternalHttpUri(apiBaseUri) ||
            RequestTimeoutSeconds is < 1 or > 30 ||
            PlaylistStartupRetrySeconds is < 0 or > 30 ||
            PlaylistStartupRetryMilliseconds is < 50 or > 1000 ||
            string.IsNullOrWhiteSpace(SourceOnDemandStartTimeout) ||
            string.IsNullOrWhiteSpace(SourceOnDemandCloseAfter))
        {
            throw new InvalidOperationException("MediaMTX Control API 必须使用 HTTPS，或仅通过回环地址使用 HTTP。");
        }

        return new Uri(apiBaseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    public Uri ValidateAndGetHlsBaseUri()
    {
        if (!Uri.TryCreate(HlsBaseUri, UriKind.Absolute, out var hlsBaseUri) ||
            !IsAllowedInternalHttpUri(hlsBaseUri) ||
            !string.IsNullOrEmpty(hlsBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(hlsBaseUri.Query) ||
            !string.IsNullOrEmpty(hlsBaseUri.Fragment))
        {
            throw new InvalidOperationException("MediaMTX HLS 上游必须使用无凭据、无查询参数的回环 HTTP 或 HTTPS 地址。");
        }

        return new Uri(hlsBaseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    public bool IsTrustedAuthCallbackAddress(IPAddress? address) =>
        address is not null &&
        (IsLoopback(address) || IsConfiguredAddress(address, TrustedAuthCallbackAddresses));

    public bool IsTrustedHlsReaderAddress(string? address) =>
        IPAddress.TryParse(address, out var parsed) &&
        (IsLoopback(parsed) || IsConfiguredAddress(parsed, TrustedHlsReaderAddresses));

    public void ValidatePublisherCredentials()
    {
        if (string.IsNullOrWhiteSpace(PublisherUsername) || PublisherUsername.Length > 64 ||
            !PublisherUsername.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
            string.IsNullOrWhiteSpace(PublisherPassword) || PublisherPassword.Length is < 32 or > 256)
        {
            throw new InvalidOperationException("MediaMTX 回放发布者凭据无效。");
        }
    }

    public void ValidateHlsReaderCredentials()
    {
        if (string.IsNullOrWhiteSpace(HlsReaderUsername) || HlsReaderUsername.Length > 64 ||
            !HlsReaderUsername.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_') ||
            string.IsNullOrWhiteSpace(HlsReaderPassword) || HlsReaderPassword.Length is < 32 or > 256 ||
            AreCredentialValuesEqual(HlsReaderPassword, PublisherPassword))
        {
            throw new InvalidOperationException("MediaMTX HLS 内部读取凭据无效，且密码不得与回放发布者凭据复用。");
        }
    }

    public bool IsPublisherCredentialsValid(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(PublisherUsername) || string.IsNullOrWhiteSpace(PublisherPassword))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
                   SHA256.HashData(Encoding.UTF8.GetBytes(PublisherUsername)),
                   SHA256.HashData(Encoding.UTF8.GetBytes(username))) &&
               CryptographicOperations.FixedTimeEquals(
                   SHA256.HashData(Encoding.UTF8.GetBytes(PublisherPassword)),
                   SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }

    public bool IsHlsReaderCredentialsValid(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(HlsReaderUsername) || string.IsNullOrWhiteSpace(HlsReaderPassword))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
                   SHA256.HashData(Encoding.UTF8.GetBytes(HlsReaderUsername)),
                   SHA256.HashData(Encoding.UTF8.GetBytes(username))) &&
               CryptographicOperations.FixedTimeEquals(
                   SHA256.HashData(Encoding.UTF8.GetBytes(HlsReaderPassword)),
                   SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }

    private static bool AreCredentialValuesEqual(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(first)),
            SHA256.HashData(Encoding.UTF8.GetBytes(second)));

    private bool IsAllowedInternalHttpUri(Uri uri) =>
        (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) &&
        (uri.IsLoopback || IsConfiguredHost(uri.Host));

    private bool IsConfiguredHost(string host) =>
        TrustedInternalHosts.Any(value =>
            !string.IsNullOrWhiteSpace(value) &&
            string.Equals(value.Trim(), host, StringComparison.OrdinalIgnoreCase));

    private static bool IsConfiguredAddress(IPAddress address, IEnumerable<string> configuredAddresses) =>
        configuredAddresses.Any(value =>
            IPAddress.TryParse(value, out var configured) &&
            NormalizeAddress(configured).Equals(NormalizeAddress(address)));

    private static bool IsLoopback(IPAddress? address) =>
        address is not null && IPAddress.IsLoopback(NormalizeAddress(address));

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

public sealed class LiveTranscodeOptions
{
    public bool Enabled { get; init; }
    public string? FfmpegExecutablePath { get; init; }
    public string MediaMtxRtspBaseUri { get; init; } = "rtsp://127.0.0.1:8554/";
    public IReadOnlyList<Guid> ValidatedRecorderIds { get; init; } = [];
    public int MaxConcurrentRelays { get; init; } = 1;
    public int StartupTimeoutSeconds { get; init; } = 20;
    public int IdleCloseAfterSeconds { get; init; } = 15;
    public int RestartBackoffSeconds { get; init; } = 2;
    public int VideoBitrateKbps { get; init; } = 4000;
    public int VideoMaxRateKbps { get; init; } = 6000;
    public int VideoBufferKbps { get; init; } = 8000;
    public int GopSize { get; init; } = 40;
    public string[] TrustedInternalHosts { get; init; } = [];

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(FfmpegExecutablePath) ||
            !Path.IsPathFullyQualified(FfmpegExecutablePath) ||
            !File.Exists(FfmpegExecutablePath) ||
            !Uri.TryCreate(MediaMtxRtspBaseUri, UriKind.Absolute, out var rtspBaseUri) ||
            !rtspBaseUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) ||
            (!rtspBaseUri.IsLoopback && !TrustedInternalHosts.Any(value =>
                !string.IsNullOrWhiteSpace(value) &&
                string.Equals(value.Trim(), rtspBaseUri.Host, StringComparison.OrdinalIgnoreCase))) ||
            !string.IsNullOrEmpty(rtspBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(rtspBaseUri.Query) ||
            !string.IsNullOrEmpty(rtspBaseUri.Fragment) ||
            rtspBaseUri.Port is < 1 or > 65535 ||
            ValidatedRecorderIds.Count == 0 ||
            ValidatedRecorderIds.Any(item => item == Guid.Empty) ||
            ValidatedRecorderIds.Distinct().Count() != ValidatedRecorderIds.Count ||
            MaxConcurrentRelays is < 1 or > 16 ||
            StartupTimeoutSeconds is < 5 or > 60 ||
            IdleCloseAfterSeconds is < 1 or > 300 ||
            RestartBackoffSeconds is < 1 or > 30 ||
            VideoBitrateKbps is < 500 or > 20_000 ||
            VideoMaxRateKbps < VideoBitrateKbps || VideoMaxRateKbps > 30_000 ||
            VideoBufferKbps < VideoMaxRateKbps || VideoBufferKbps > 60_000 ||
            GopSize is < 10 or > 300)
        {
            throw new InvalidOperationException(
                "实时主码流转码配置无效。FFmpeg 必须是受控绝对路径，MediaMTX RTSP 基址必须是无凭据的回环地址，并至少配置一台已验收录像机。");
        }
    }

    public bool IsEnabledFor(Guid recorderId) =>
        Enabled && ValidatedRecorderIds.Contains(recorderId);

    public Uri BuildMediaMtxUri(string pathName)
    {
        Validate();
        if (!LiveTranscodePath.IsInternalSource(pathName) && !LiveTranscodePath.IsPublicMain(pathName))
        {
            throw new ArgumentException("实时转码路径格式无效。", nameof(pathName));
        }

        var baseUri = new Uri(MediaMtxRtspBaseUri, UriKind.Absolute);
        var builder = new UriBuilder(baseUri)
        {
            Path = $"{baseUri.AbsolutePath.TrimEnd('/')}/{pathName}",
            Query = string.Empty,
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        };
        return builder.Uri;
    }
}

public static class LiveTranscodePath
{
    private const string InternalPrefix = "internal/live-source/";
    private const string PublicPrefix = "live/";

    public static string BuildInternalSource(Guid cameraId) =>
        $"{InternalPrefix}{cameraId:N}/main";

    public static string BuildPublicMain(Guid cameraId) =>
        $"{PublicPrefix}{cameraId:N}/main";

    public static bool IsInternalSource(string? pathName) =>
        IsCameraPath(pathName, InternalPrefix);

    public static bool IsPublicMain(string? pathName) =>
        IsCameraPath(pathName, PublicPrefix);

    private static bool IsCameraPath(string? pathName, string prefix) =>
        pathName is not null &&
        pathName.Length == prefix.Length + 32 + "/main".Length &&
        pathName.StartsWith(prefix, StringComparison.Ordinal) &&
        pathName.EndsWith("/main", StringComparison.Ordinal) &&
        Guid.TryParseExact(pathName.AsSpan(prefix.Length, 32), "N", out _);
}
