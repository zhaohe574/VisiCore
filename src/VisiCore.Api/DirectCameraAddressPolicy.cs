using System.Text.Json;
using VisiCore.Core;

namespace VisiCore.Api;

public sealed record DirectCameraAddress(
    string Host,
    int Port,
    bool UseTls,
    string MainPath,
    string SubPath,
    string MainUrl,
    string SubUrl,
    string StreamingChannelMap);

public static class DirectCameraAddressPolicy
{
    private const int MaximumDefaultMappingChannel = 21_474_836;

    private static readonly IReadOnlySet<string> SensitiveQueryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "user", "username", "password", "passwd", "pwd", "token", "access_token", "auth", "auth_token",
        "apikey", "api_key", "key", "secret", "signature", "credential", "session", "ticket"
    };

    private static readonly string[] SensitiveQueryKeyFragments =
    [
        "password", "passwd", "pwd", "token", "auth", "apikey", "api_key", "secret", "signature",
        "credential", "session", "ticket", "username", "userid", "account", "login"
    ];

    public static bool TryNormalize(
        string? mainStreamUrl,
        string? subStreamUrl,
        out DirectCameraAddress? address,
        out string validationError)
    {
        address = null;
        if (!TryParse(mainStreamUrl, out var main, out validationError))
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(subStreamUrl))
        {
            subStreamUrl = mainStreamUrl;
        }
        if (!TryParse(subStreamUrl, out var sub, out validationError))
        {
            return false;
        }
        if (!main!.Host.Equals(sub!.Host, StringComparison.OrdinalIgnoreCase) || main.Port != sub.Port ||
            !main.Scheme.Equals(sub.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            validationError = "主、子码流地址必须使用同一主机、端口和传输协议。";
            return false;
        }

        var mainPath = BuildPath(main);
        var subPath = BuildPath(sub);
        var streamingMap = JsonSerializer.Serialize(new { main = mainPath, sub = subPath });
        if (streamingMap.Length > 512)
        {
            validationError = "摄像头码流路径过长。";
            return false;
        }

        address = new DirectCameraAddress(
            main.Host,
            main.Port,
            main.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase),
            mainPath,
            subPath,
            WithoutUserInfo(main).AbsoluteUri,
            WithoutUserInfo(sub).AbsoluteUri,
            streamingMap);
        validationError = string.Empty;
        return true;
    }

    public static bool TryCreateDefaultStreamingMap(int inputChannelNumber, out string streamingChannelMap)
    {
        streamingChannelMap = "{}";
        if (inputChannelNumber is < 1 or > MaximumDefaultMappingChannel)
        {
            return false;
        }

        var channelBase = inputChannelNumber * 100;
        streamingChannelMap = JsonSerializer.Serialize(new { main = channelBase + 1, sub = channelBase + 2 });
        return true;
    }

    public static bool TryValidateStreamingMap(string? streamingChannelMap, out string normalized, out string validationError) =>
        TryValidateStreamingMap(streamingChannelMap, null, out normalized, out validationError);

    public static bool TryValidateStreamingMap(
        string? streamingChannelMap,
        RecorderEndpointRegistration? rtspEndpoint,
        out string normalized,
        out string validationError)
    {
        normalized = "{}";
        validationError = string.Empty;
        if (string.IsNullOrWhiteSpace(streamingChannelMap))
        {
            validationError = "码流映射不能为空。";
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(streamingChannelMap);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("main", out var main) ||
                !document.RootElement.TryGetProperty("sub", out var sub) ||
                !TryValidateStreamMapping(main, rtspEndpoint, out validationError) ||
                !TryValidateStreamMapping(sub, rtspEndpoint, out validationError))
            {
                if (string.IsNullOrWhiteSpace(validationError))
                {
                    validationError = "码流映射必须包含有效的 main 和 sub。";
                }
                return false;
            }
            normalized = JsonSerializer.Serialize(document.RootElement);
            if (normalized.Length > 512)
            {
                validationError = "码流映射超过长度限制。";
                return false;
            }
            validationError = string.Empty;
            return true;
        }
        catch (JsonException)
        {
            validationError = "码流映射必须是有效 JSON。";
            return false;
        }
    }

    private static bool TryParse(string? value, out Uri? uri, out string validationError)
    {
        uri = null;
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) ||
            (!parsed.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
             !parsed.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase)))
        {
            validationError = "直连地址必须是绝对 RTSP 或 RTSPS 地址。";
            return false;
        }
        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            validationError = "直连地址不能包含用户名或密码，请使用受保护的凭据引用。";
            return false;
        }
        if (ContainsSensitiveQueryKey(parsed.Query))
        {
            validationError = "直连地址不能在查询参数中携带用户名、密码或访问令牌。";
            return false;
        }
        if (!string.IsNullOrEmpty(parsed.Fragment) || !RecorderEndpointHostPolicy.IsValidHost(parsed.Host))
        {
            validationError = "直连地址的主机或片段无效。";
            return false;
        }
        var port = parsed.IsDefaultPort || parsed.Port < 1 ? 554 : parsed.Port;
        if (port is < 1 or > 65535 || string.IsNullOrWhiteSpace(parsed.AbsolutePath) || parsed.AbsolutePath == "/")
        {
            validationError = "直连地址必须包含有效端口和码流路径。";
            return false;
        }
        var builder = new UriBuilder(parsed) { Port = port, UserName = string.Empty, Password = string.Empty, Fragment = string.Empty };
        uri = builder.Uri;
        validationError = string.Empty;
        return true;
    }

    private static string BuildPath(Uri uri)
    {
        var value = uri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped);
        return "/" + value.TrimStart('/');
    }

    private static Uri WithoutUserInfo(Uri uri) =>
        new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri;

    private static bool ContainsSensitiveQueryKey(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.Split('=', 2)[0])
            .Select(Uri.UnescapeDataString)
            .Select(item => item.Trim().ToLowerInvariant())
            .Any(item => SensitiveQueryKeys.Contains(item) || SensitiveQueryKeyFragments.Any(item.Contains));

    private static bool TryValidateStreamMapping(
        JsonElement value,
        RecorderEndpointRegistration? rtspEndpoint,
        out string validationError)
    {
        validationError = string.Empty;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0)
        {
            return true;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            validationError = "码流映射只允许正整数、绝对 RTSP 地址或以斜杠开头的路径。";
            return false;
        }

        var mapping = value.GetString()!.Trim();
        if (Uri.TryCreate(mapping, UriKind.Absolute, out var absolute))
        {
            if ((!absolute.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
                 !absolute.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase)) ||
                !string.IsNullOrEmpty(absolute.UserInfo) || !string.IsNullOrEmpty(absolute.Fragment) ||
                ContainsSensitiveQueryKey(absolute.Query) || !RecorderEndpointHostPolicy.IsValidHost(absolute.Host))
            {
                validationError = "绝对码流地址必须是无内嵌凭据的 RTSP 或 RTSPS 地址。";
                return false;
            }
            if (rtspEndpoint is not null &&
                (!absolute.Host.Equals(rtspEndpoint.Host, StringComparison.OrdinalIgnoreCase) ||
                 absolute.Port != rtspEndpoint.Port ||
                 absolute.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase) != rtspEndpoint.UseTls))
            {
                validationError = "绝对码流地址不能越过已登记的 RTSP 端点。";
                return false;
            }
            return true;
        }

        if (!mapping.StartsWith("/", StringComparison.Ordinal) || mapping.StartsWith("//", StringComparison.Ordinal) ||
            mapping.Contains('\\') || mapping.Contains('#'))
        {
            validationError = "字符串码流映射必须是绝对 RTSP 地址或以单个斜杠开头的路径。";
            return false;
        }
        var queryIndex = mapping.IndexOf("?", StringComparison.Ordinal);
        if (queryIndex >= 0 && ContainsSensitiveQueryKey(mapping[queryIndex..]))
        {
            validationError = "码流路径不能在查询参数中携带凭据、令牌或签名。";
            return false;
        }
        return true;
    }
}
