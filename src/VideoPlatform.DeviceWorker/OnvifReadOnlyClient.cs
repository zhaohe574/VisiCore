using System.Net;
using System.Collections.Concurrent;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using VideoPlatform.Core;

namespace VideoPlatform.DeviceWorker;

public sealed class OnvifReadOnlyClient(
    IRecorderCredentialResolver credentialResolver,
    OnvifReadOnlyOptions? options = null,
    HttpMessageHandler? messageHandler = null)
{
    private const string DeviceNamespace = "http://www.onvif.org/ver10/device/wsdl";
    private const string MediaNamespace = "http://www.onvif.org/ver10/media/wsdl";
    private const string Media2Namespace = "http://www.onvif.org/ver20/media/wsdl";
    private const string RecordingNamespace = "http://www.onvif.org/ver10/recording/wsdl";
    private const string SearchNamespace = "http://www.onvif.org/ver10/search/wsdl";
    private const string ReplayNamespace = "http://www.onvif.org/ver10/replay/wsdl";
    private const string SchemaNamespace = "http://www.onvif.org/ver10/schema";
    private readonly OnvifReadOnlyOptions _options = options ?? new OnvifReadOnlyOptions();

    public async Task<OnvifDiscovery> DiscoverAsync(WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        var endpoint = GetEndpoint(assignment, "Onvif");
        var recorder = ToRecorder(assignment, endpoint);
        var credential = await credentialResolver.ResolveAsync(recorder, cancellationToken);
        using var client = CreateClient(credential, endpoint);
        var deviceService = CreateDeviceServiceUri(endpoint);
        var services = await GetServicesAsync(client, deviceService, cancellationToken);
        var mediaServiceInfo = SelectMediaService(deviceService, services);
        var hasDeclaredProfileG = HasDeclaredProfileG(deviceService, services);
        var mediaService = new Uri(mediaServiceInfo.XAddr, UriKind.Absolute);
        var mediaVersion = mediaServiceInfo.Namespace;
        var profiles = await GetProfilesAsync(client, mediaService, mediaVersion, cancellationToken);
        if (profiles.Count == 0)
        {
            throw new OnvifProtocolException("ONVIF Media 服务未返回可用 Profile。");
        }
        if (profiles.Count > _options.MaxProfiles)
        {
            throw new OnvifProtocolException("ONVIF Media Profile 数量超过当前 Worker 上限。");
        }

        var rtspEndpoint = GetEndpoint(assignment, "Rtsp");
        var plans = profiles.GroupBy(item => item.SourceToken, StringComparer.Ordinal).OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var orderedProfiles = group.OrderByDescending(item => item.ResolutionPixels)
                .ThenByDescending(item => item.BitrateLimit)
                .ThenBy(item => item.Token, StringComparer.Ordinal)
                .ToList();
                var mainProfile = orderedProfiles[0];
                var ptzProfile = orderedProfiles.FirstOrDefault(item => item.SupportsPtz);
                return new OnvifChannelPlan(
                    group.Key,
                    mainProfile,
                    orderedProfiles.Count > 1 ? orderedProfiles[^1] : mainProfile,
                    ptzProfile?.Token);
            }).ToList();
        var channels = new ConcurrentBag<OnvifDiscoveredChannel>();
        await Parallel.ForEachAsync(plans, new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrentStreamUriRequests,
            CancellationToken = cancellationToken
        }, async (plan, token) =>
        {
            var mainUri = await GetStreamUriAsync(client, mediaService, mediaVersion, plan.MainProfile.Token, token);
            var subUri = plan.MainProfile.Token == plan.SubProfile.Token
                ? mainUri
                : await GetStreamUriAsync(client, mediaService, mediaVersion, plan.SubProfile.Token, token);
            ValidateRtspUri(mainUri, rtspEndpoint);
            ValidateRtspUri(subUri, rtspEndpoint);
            channels.Add(new OnvifDiscoveredChannel(
                plan.SourceToken,
                plan.MainProfile.Name,
                !string.IsNullOrWhiteSpace(plan.PtzProfileToken),
                mainUri,
                subUri,
                plan.PtzProfileToken));
        });

        return new OnvifDiscovery(
            (mediaVersion == Media2Namespace ? "onvif-media2-readonly-v1" : "onvif-media-readonly-v1") +
            (hasDeclaredProfileG ? "+profile-g-declared" : string.Empty),
            hasDeclaredProfileG,
            channels.OrderBy(item => item.SourceToken, StringComparer.Ordinal).ToList());
    }

    public async Task PingAsync(WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        var endpoint = GetEndpoint(assignment, "Onvif");
        var recorder = ToRecorder(assignment, endpoint);
        var credential = await credentialResolver.ResolveAsync(recorder, cancellationToken);
        using var client = CreateClient(credential, endpoint);
        var payload = "<tds:GetDeviceInformation xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"/>";
        await SendSoapAsync(client, CreateDeviceServiceUri(endpoint), DeviceNamespace + "/GetDeviceInformation", payload, cancellationToken);
    }

    public async Task<DateTimeOffset> GetSystemTimeAsync(WorkerRecorderAssignment assignment, CancellationToken cancellationToken)
    {
        var endpoint = GetEndpoint(assignment, "Onvif");
        var recorder = ToRecorder(assignment, endpoint);
        var credential = await credentialResolver.ResolveAsync(recorder, cancellationToken);
        using var client = CreateClient(credential, endpoint);
        const string payload = "<tds:GetSystemDateAndTime xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"/>";
        var document = await SendSoapAsync(client, CreateDeviceServiceUri(endpoint), DeviceNamespace + "/GetSystemDateAndTime", payload, cancellationToken);
        var utcDateTime = document.Descendants().FirstOrDefault(item => item.Name.LocalName == "UTCDateTime")
            ?? throw new OnvifProtocolException("ONVIF 系统时间响应缺少 UTCDateTime。 ");
        var date = utcDateTime.Elements().FirstOrDefault(item => item.Name.LocalName == "Date")
            ?? throw new OnvifProtocolException("ONVIF 系统时间响应缺少日期。 ");
        var time = utcDateTime.Elements().FirstOrDefault(item => item.Name.LocalName == "Time")
            ?? throw new OnvifProtocolException("ONVIF 系统时间响应缺少时间。 ");
        try
        {
            return new DateTimeOffset(
                ReadRequiredInt(date, "Year"),
                ReadRequiredInt(date, "Month"),
                ReadRequiredInt(date, "Day"),
                ReadRequiredInt(time, "Hour"),
                ReadRequiredInt(time, "Minute"),
                ReadRequiredInt(time, "Second"),
                TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new OnvifProtocolException("ONVIF 系统时间响应包含无效日期或时间。 ", innerException: exception);
        }
    }

    private HttpClient CreateClient(NetworkCredential credential, WorkerRecorderEndpoint endpoint)
    {
        DeviceCertificatePolicy.EnsureTlsConfiguration(endpoint, _options.AllowUntrustedCertificate);
        if (messageHandler is not null)
        {
            return new HttpClient(messageHandler, disposeHandler: false) { Timeout = _options.RequestTimeout };
        }
        var handler = new HttpClientHandler
        {
            Credentials = credential,
            PreAuthenticate = true,
            UseProxy = false,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = endpoint.UseTls
                ? (_, certificate, _, sslPolicyErrors) => DeviceCertificatePolicy.IsServerCertificateAccepted(
                    endpoint,
                    certificate,
                    sslPolicyErrors,
                    _options.AllowUntrustedCertificate)
                : null
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = _options.RequestTimeout };
    }

    private static async Task<IReadOnlyList<OnvifService>> GetServicesAsync(HttpClient client, Uri deviceService, CancellationToken cancellationToken)
    {
        const string payload = "<tds:GetServices xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"><tds:IncludeCapability>false</tds:IncludeCapability></tds:GetServices>";
        var document = await SendSoapAsync(client, deviceService, DeviceNamespace + "/GetServices", payload, cancellationToken);
        return document.Descendants().Where(item => item.Name.LocalName == "Service")
            .Select(item =>
            {
                var serviceNamespace = ReadChildValue(item, "Namespace");
                var serviceAddress = ReadChildValue(item, "XAddr");
                return string.IsNullOrWhiteSpace(serviceNamespace) || string.IsNullOrWhiteSpace(serviceAddress)
                    ? null
                    : new OnvifService(serviceNamespace, serviceAddress);
            })
            .Where(item => item is not null)
            .Cast<OnvifService>()
            .ToList();
    }

    private static OnvifService SelectMediaService(Uri deviceService, IReadOnlyList<OnvifService> services)
    {
        var selected = services.FirstOrDefault(item => item.Namespace == Media2Namespace)
            ?? services.FirstOrDefault(item => item.Namespace == MediaNamespace)
            ?? throw new OnvifProtocolException("ONVIF 设备未声明 Media 或 Media2 服务。");
        if (!Uri.TryCreate(selected.XAddr, UriKind.Absolute, out var mediaService) ||
            !IsSafeServiceUri(mediaService, deviceService))
        {
            throw new OnvifProtocolException("ONVIF Media 服务地址无效或越过已登记的设备主机。");
        }
        return selected;
    }

    private static bool HasDeclaredProfileG(Uri deviceService, IReadOnlyList<OnvifService> services)
    {
        var profileGNamespaces = new[] { RecordingNamespace, SearchNamespace, ReplayNamespace };
        foreach (var serviceNamespace in profileGNamespaces)
        {
            var service = services.FirstOrDefault(item => string.Equals(item.Namespace, serviceNamespace, StringComparison.Ordinal));
            if (service is null || !Uri.TryCreate(service.XAddr, UriKind.Absolute, out var serviceUri) ||
                !IsSafeServiceUri(serviceUri, deviceService))
            {
                return false;
            }
        }
        return true;
    }

    private static async Task<IReadOnlyList<OnvifProfile>> GetProfilesAsync(
        HttpClient client,
        Uri mediaService,
        string mediaVersion,
        CancellationToken cancellationToken)
    {
        var prefix = mediaVersion == Media2Namespace ? "tr2" : "trt";
        var payload = $"<{prefix}:GetProfiles xmlns:{prefix}=\"{mediaVersion}\"/>";
        var document = await SendSoapAsync(client, mediaService, mediaVersion + "/GetProfiles", payload, cancellationToken);
        return document.Descendants().Where(item => item.Name.LocalName == "Profiles")
            .Select(ParseProfile)
            .Where(item => item is not null)
            .Cast<OnvifProfile>()
            .ToList();
    }

    private static OnvifProfile? ParseProfile(XElement profile)
    {
        var token = profile.Attributes().FirstOrDefault(item => item.Name.LocalName == "token")?.Value;
        var source = profile.Descendants().FirstOrDefault(item => item.Name.LocalName == "VideoSourceConfiguration");
        var sourceToken = source is null ? null : ReadChildValue(source, "SourceToken");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(sourceToken))
        {
            return null;
        }
        var resolution = profile.Descendants().FirstOrDefault(item => item.Name.LocalName == "Resolution");
        var width = TryReadInt(resolution, "Width");
        var height = TryReadInt(resolution, "Height");
        var rateControl = profile.Descendants().FirstOrDefault(item => item.Name.LocalName == "RateControl");
        return new OnvifProfile(
            token,
            sourceToken,
            ReadChildValue(profile, "Name") ?? ReadChildValue(source, "Name") ?? $"ONVIF {sourceToken}",
            checked((long)width * height),
            TryReadInt(rateControl, "BitrateLimit"),
            profile.Descendants().Any(item => item.Name.LocalName == "PTZConfiguration"));
    }

    private static async Task<Uri> GetStreamUriAsync(
        HttpClient client,
        Uri mediaService,
        string mediaVersion,
        string profileToken,
        CancellationToken cancellationToken)
    {
        var escapedToken = SecurityElement.Escape(profileToken) ?? throw new OnvifProtocolException("ONVIF Profile token 无效。");
        string payload;
        if (mediaVersion == Media2Namespace)
        {
            payload = $"<tr2:GetStreamUri xmlns:tr2=\"{Media2Namespace}\"><tr2:Protocol>RtspUnicast</tr2:Protocol><tr2:ProfileToken>{escapedToken}</tr2:ProfileToken></tr2:GetStreamUri>";
        }
        else
        {
            payload = $"<trt:GetStreamUri xmlns:trt=\"{MediaNamespace}\" xmlns:tt=\"{SchemaNamespace}\"><trt:StreamSetup><tt:Stream>RTP-Unicast</tt:Stream><tt:Transport><tt:Protocol>RTSP</tt:Protocol></tt:Transport></trt:StreamSetup><trt:ProfileToken>{escapedToken}</trt:ProfileToken></trt:GetStreamUri>";
        }
        var document = await SendSoapAsync(client, mediaService, mediaVersion + "/GetStreamUri", payload, cancellationToken);
        var value = document.Descendants().FirstOrDefault(item => item.Name.LocalName == "Uri")?.Value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new OnvifProtocolException("ONVIF Media 服务返回的 RTSP 地址无效。");
        }
        return uri;
    }

    private static async Task<XDocument> SendSoapAsync(
        HttpClient client,
        Uri serviceUri,
        string action,
        string body,
        CancellationToken cancellationToken)
    {
        var envelope = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>{body}</s:Body></s:Envelope>";
        using var content = new StringContent(envelope, Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/soap+xml");
        content.Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("action", $"\"{action}\""));
        using var response = await client.PostAsync(serviceUri, content, cancellationToken);
        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OnvifProtocolException($"ONVIF 只读请求失败，状态码：{(int)response.StatusCode}。", response.StatusCode);
        }
        try
        {
            using var reader = XmlReader.Create(new StringReader(responseXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Descendants().Any(item => item.Name.LocalName == "Fault"))
            {
                throw new OnvifProtocolException("ONVIF 设备返回 SOAP Fault。");
            }
            return document;
        }
        catch (XmlException exception)
        {
            throw new OnvifProtocolException("ONVIF 设备返回的 XML 无法解析。", innerException: exception);
        }
    }

    private static WorkerRecorderEndpoint GetEndpoint(WorkerRecorderAssignment assignment, string protocol) =>
        assignment.Endpoints.SingleOrDefault(item => item.Protocol.Equals(protocol, StringComparison.OrdinalIgnoreCase))
        ?? throw new OnvifProtocolException($"录像机分配缺少 {protocol} 端点。");

    private static RecorderCredentialTarget ToRecorder(WorkerRecorderAssignment assignment, WorkerRecorderEndpoint endpoint) =>
        new(assignment.RecorderId, assignment.RecorderName, assignment.Vendor, endpoint.Host, endpoint.Port, assignment.AdapterType, endpoint.CredentialReference);

    private static Uri CreateDeviceServiceUri(WorkerRecorderEndpoint endpoint)
    {
        if (!RecorderEndpointHostPolicy.IsValidHost(endpoint.Host) || endpoint.Port is < 1 or > 65535)
        {
            throw new OnvifProtocolException("ONVIF 端点无效。");
        }
        return new UriBuilder(endpoint.UseTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, endpoint.Host, endpoint.Port, "/onvif/device_service").Uri;
    }

    private static void ValidateRtspUri(Uri uri, WorkerRecorderEndpoint endpoint)
    {
        if ((!uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) && !uri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase)) ||
            !uri.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase) || uri.Port != endpoint.Port || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new OnvifProtocolException("ONVIF 返回的 RTSP 地址与已登记的 RTSP 端点不一致。");
        }
    }

    private static bool IsSafeServiceUri(Uri serviceUri, Uri deviceService) =>
        (serviceUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || serviceUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
        serviceUri.Scheme.Equals(deviceService.Scheme, StringComparison.OrdinalIgnoreCase) &&
        serviceUri.Host.Equals(deviceService.Host, StringComparison.OrdinalIgnoreCase) &&
        serviceUri.Port == deviceService.Port &&
        string.IsNullOrEmpty(serviceUri.UserInfo);

    private static string? ReadChildValue(XElement? element, string localName) =>
        element?.Elements().FirstOrDefault(item => item.Name.LocalName == localName)?.Value;

    private static int TryReadInt(XElement? element, string localName) =>
        int.TryParse(ReadChildValue(element, localName), out var value) && value > 0 ? value : 0;

    private static int ReadRequiredInt(XElement element, string localName)
    {
        if (!int.TryParse(ReadChildValue(element, localName), out var value))
        {
            throw new OnvifProtocolException($"ONVIF 系统时间响应缺少或包含无效的 {localName}。 ");
        }
        return value;
    }
}

public sealed record OnvifDiscovery(
    string Version,
    bool HasDeclaredProfileG,
    IReadOnlyList<OnvifDiscoveredChannel> Channels);

public sealed record OnvifDiscoveredChannel(
    string SourceToken,
    string Name,
    bool SupportsPtz,
    Uri MainUri,
    Uri SubUri,
    string? PtzProfileToken);

public sealed class OnvifReadOnlyOptions
{
    public bool AllowUntrustedCertificate { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxProfiles { get; init; } = 4096;
    public int MaxConcurrentStreamUriRequests { get; init; } = 4;

    public bool TryValidate(out string validationError)
    {
        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromMinutes(1))
        {
            validationError = "Onvif:RequestTimeout 必须在 1 秒到 1 分钟之间。";
            return false;
        }
        if (MaxProfiles is < 1 or > 4096)
        {
            validationError = "Onvif:MaxProfiles 必须在 1 到 4096 之间。";
            return false;
        }
        if (MaxConcurrentStreamUriRequests is < 1 or > 16)
        {
            validationError = "Onvif:MaxConcurrentStreamUriRequests 必须在 1 到 16 之间。";
            return false;
        }
        validationError = string.Empty;
        return true;
    }
}

public sealed class OnvifProtocolException : Exception
{
    public OnvifProtocolException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

internal sealed record OnvifService(string Namespace, string XAddr);

internal sealed record OnvifProfile(
    string Token,
    string SourceToken,
    string Name,
    long ResolutionPixels,
    int BitrateLimit,
    bool SupportsPtz);

internal sealed record OnvifChannelPlan(
    string SourceToken,
    OnvifProfile MainProfile,
    OnvifProfile SubProfile,
    string? PtzProfileToken);
