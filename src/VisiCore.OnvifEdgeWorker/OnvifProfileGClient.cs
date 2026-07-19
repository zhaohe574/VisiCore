using System.Net;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public interface IOnvifProfileGClient
{
    Task<OnvifProfileGProbeResult> ProbeAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        CancellationToken cancellationToken);

    Task<OnvifProfileGSearchResult> SearchAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        RecordingSearchCommandPayload payload,
        CancellationToken cancellationToken);

    Task<Uri> GetReplayUriAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        CancellationToken cancellationToken);

    Task<OnvifProfileGReplaySource> GetReplaySourceAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        CancellationToken cancellationToken);
}

public sealed class OnvifProfileGClient(
    IOnvifEdgeCredentialResolver credentialResolver,
    OnvifEdgeOptions options,
    HttpMessageHandler? messageHandler = null) : IOnvifProfileGClient
{
    private const string DeviceNamespace = "http://www.onvif.org/ver10/device/wsdl";
    private const string RecordingNamespace = "http://www.onvif.org/ver10/recording/wsdl";
    private const string SearchNamespace = "http://www.onvif.org/ver10/search/wsdl";
    private const string ReplayNamespace = "http://www.onvif.org/ver10/replay/wsdl";
    private const string SchemaNamespace = "http://www.onvif.org/ver10/schema";
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };

    public async Task<OnvifProfileGProbeResult> ProbeAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        CancellationToken cancellationToken)
    {
        options.ProfileG.ValidateEnabled();
        var context = CreateContext(assignment);
        using (context.Client)
        {
            var services = await GetServicesAsync(context.Client, context.DeviceService, cancellationToken);
            var recordingService = GetProfileGServiceUri(services, RecordingNamespace, context.DeviceService);
            _ = GetProfileGServiceUri(services, SearchNamespace, context.DeviceService);
            _ = GetProfileGServiceUri(services, ReplayNamespace, context.DeviceService);
            var sourceToken = GetSourceToken(camera);
            var recordings = await GetRecordingsAsync(context.Client, recordingService, cancellationToken);
            var recordingToken = FindUniqueRecordingToken(recordings, sourceToken);
            return new OnvifProfileGProbeResult(recordingToken, sourceToken);
        }
    }

    public async Task<OnvifProfileGSearchResult> SearchAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        RecordingSearchCommandPayload payload,
        CancellationToken cancellationToken)
    {
        options.ProfileG.ValidateEnabled();
        ValidateSearchPayload(payload, camera.CameraId);
        var context = CreateContext(assignment);
        using (context.Client)
        {
            var services = await GetServicesAsync(context.Client, context.DeviceService, cancellationToken);
            var recordingService = GetProfileGServiceUri(services, RecordingNamespace, context.DeviceService);
            var searchService = GetProfileGServiceUri(services, SearchNamespace, context.DeviceService);
            _ = GetProfileGServiceUri(services, ReplayNamespace, context.DeviceService);
            var sourceToken = GetSourceToken(camera);
            var recordings = await GetRecordingsAsync(context.Client, recordingService, cancellationToken);
            var recordingToken = FindUniqueRecordingToken(recordings, sourceToken);
            var result = await SearchRecordingsAsync(
                context.Client,
                searchService,
                recordingToken,
                sourceToken,
                Math.Clamp(payload.MaxResults ?? options.ProfileG.MaxSearchResults, 1, options.ProfileG.MaxSearchResults),
                cancellationToken);
            var overlappingSegments = result
                .Where(item => item.StartedAt <= payload.EndedAt && item.EndedAt >= payload.StartedAt)
                .ToList();
            return new OnvifProfileGSearchResult(
                camera.CameraId,
                recordingToken,
                sourceToken,
                overlappingSegments);
        }
    }

    public async Task<Uri> GetReplayUriAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        CancellationToken cancellationToken)
    {
        options.ProfileG.ValidateEnabled();
        var context = CreateContext(assignment);
        using (context.Client)
        {
            var services = await GetServicesAsync(context.Client, context.DeviceService, cancellationToken);
            var recordingService = GetProfileGServiceUri(services, RecordingNamespace, context.DeviceService);
            var replayService = GetProfileGServiceUri(services, ReplayNamespace, context.DeviceService);
            _ = GetProfileGServiceUri(services, SearchNamespace, context.DeviceService);
            var sourceToken = GetSourceToken(camera);
            var recordings = await GetRecordingsAsync(context.Client, recordingService, cancellationToken);
            var recordingToken = FindUniqueRecordingToken(recordings, sourceToken);
            var escapedRecordingToken = EscapeRequired(recordingToken, "录像令牌");
            var payload = $"<trp:GetReplayUri xmlns:trp=\"{ReplayNamespace}\" xmlns:tt=\"{SchemaNamespace}\"><trp:StreamSetup><tt:Stream>RTP-Unicast</tt:Stream><tt:Transport><tt:Protocol>RTSP</tt:Protocol></tt:Transport></trp:StreamSetup><trp:RecordingToken>{escapedRecordingToken}</trp:RecordingToken></trp:GetReplayUri>";
            var document = await SendSoapAsync(context.Client, replayService, ReplayNamespace + "/GetReplayUri", payload, cancellationToken);
            var value = document.Descendants().FirstOrDefault(item => item.Name.LocalName == "Uri")?.Value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var replayUri))
            {
                throw new InvalidOperationException("ONVIF Profile G 未返回有效的 Replay URI。 ");
            }
            ValidateReplayUri(replayUri, GetEndpoint(assignment, "Rtsp"));
            return replayUri;
        }
    }

    public async Task<OnvifProfileGReplaySource> GetReplaySourceAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        CancellationToken cancellationToken)
    {
        var endpoint = GetEndpoint(assignment, "Rtsp");
        var credential = GetCredential(assignment, endpoint.CredentialReference);
        var replayUri = await GetReplayUriAsync(assignment, camera, cancellationToken);
        if (string.IsNullOrWhiteSpace(credential.UserName) || string.IsNullOrWhiteSpace(credential.Password))
        {
            throw new InvalidOperationException("ONVIF Profile G 回放端点缺少 RTSP 凭据。 ");
        }
        return new OnvifProfileGReplaySource(replayUri, credential.UserName, credential.Password);
    }

    private OnvifProfileGContext CreateContext(WorkerRecorderAssignment assignment)
    {
        var endpoint = GetEndpoint(assignment, "Onvif");
        var credential = GetCredential(assignment, endpoint.CredentialReference);
        var client = CreateClient(credential, endpoint);
        try
        {
            return new OnvifProfileGContext(client, CreateDeviceServiceUri(endpoint));
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<IReadOnlyList<OnvifRecordingDescriptor>> GetRecordingsAsync(
        HttpClient client,
        Uri recordingService,
        CancellationToken cancellationToken)
    {
        const string payload = "<trc:GetRecordings xmlns:trc=\"http://www.onvif.org/ver10/recording/wsdl\"/>";
        var document = await SendSoapAsync(client, recordingService, RecordingNamespace + "/GetRecordings", payload, cancellationToken);
        var descriptors = document.Descendants().Where(item => item.Name.LocalName == "RecordingItem")
            .Select(item => new OnvifRecordingDescriptor(
                ReadChildValue(item, "RecordingToken"),
                item.Descendants()
                    .Where(child => child.Name.LocalName is "SourceToken" or "SourceId")
                    .Select(child => child.Value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()))
            .Where(item => !string.IsNullOrWhiteSpace(item.RecordingToken))
            .ToList();
        if (descriptors.Count == 0)
        {
            throw new InvalidOperationException("ONVIF Profile G 未返回录像令牌。 ");
        }
        return descriptors;
    }

    private async Task<IReadOnlyList<OnvifProfileGSegment>> SearchRecordingsAsync(
        HttpClient client,
        Uri searchService,
        string recordingToken,
        string sourceToken,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var escapedRecordingToken = EscapeRequired(recordingToken, "录像令牌");
        var escapedSourceToken = EscapeRequired(sourceToken, "录像源令牌");
        var findPayload = $"<tse:FindRecordings xmlns:tse=\"{SearchNamespace}\" xmlns:tt=\"{SchemaNamespace}\"><tse:Scope><tt:IncludedSources><tt:Token>{escapedSourceToken}</tt:Token></tt:IncludedSources><tt:IncludedRecordings><tt:Token>{escapedRecordingToken}</tt:Token></tt:IncludedRecordings></tse:Scope><tse:MaxMatches>{maxResults}</tse:MaxMatches><tse:KeepAliveTime>PT10S</tse:KeepAliveTime></tse:FindRecordings>";
        var findResponse = await SendSoapAsync(client, searchService, SearchNamespace + "/FindRecordings", findPayload, cancellationToken);
        var searchToken = findResponse.Descendants().FirstOrDefault(item => item.Name.LocalName == "SearchToken")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(searchToken) || searchToken.Length > 512)
        {
            throw new InvalidOperationException("ONVIF Profile G 未返回有效的检索令牌。 ");
        }

        var escapedSearchToken = EscapeRequired(searchToken, "检索令牌");
        var segments = new List<OnvifProfileGSegment>();
        try
        {
            for (var poll = 0; poll < options.ProfileG.MaxSearchPolls && segments.Count < maxResults; poll++)
            {
                var resultPayload = $"<tse:GetRecordingSearchResults xmlns:tse=\"{SearchNamespace}\"><tse:SearchToken>{escapedSearchToken}</tse:SearchToken><tse:MinResults>0</tse:MinResults><tse:MaxResults>{maxResults - segments.Count}</tse:MaxResults><tse:WaitTime>PT1S</tse:WaitTime></tse:GetRecordingSearchResults>";
                var resultDocument = await SendSoapAsync(client, searchService, SearchNamespace + "/GetRecordingSearchResults", resultPayload, cancellationToken);
                segments.AddRange(ParseSegments(resultDocument, recordingToken, sourceToken));
                var state = resultDocument.Descendants().FirstOrDefault(item => item.Name.LocalName == "SearchState")?.Value;
                if (!string.Equals(state, "Searching", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(state, "Queued", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }
        finally
        {
            var endPayload = $"<tse:EndSearch xmlns:tse=\"{SearchNamespace}\"><tse:SearchToken>{escapedSearchToken}</tse:SearchToken></tse:EndSearch>";
            try
            {
                await SendSoapAsync(client, searchService, SearchNamespace + "/EndSearch", endPayload, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 释放检索会话失败不能覆盖已得到的业务结果；下一次操作仍会走新的短生命周期搜索。
            }
        }

        return segments
            .Where(item => item.EndedAt >= item.StartedAt)
            .OrderBy(item => item.StartedAt)
            .ThenBy(item => item.TrackToken, StringComparer.Ordinal)
            .DistinctBy(item => new { item.RecordingToken, item.TrackToken, item.StartedAt, item.EndedAt })
            .Take(maxResults)
            .ToList();
    }

    private static IEnumerable<OnvifProfileGSegment> ParseSegments(
        XDocument document,
        string expectedRecordingToken,
        string expectedSourceToken)
    {
        foreach (var information in document.Descendants().Where(item => item.Name.LocalName == "RecordingInformation"))
        {
            var recordingToken = ReadChildValue(information, "RecordingToken");
            if (!string.Equals(recordingToken, expectedRecordingToken, StringComparison.Ordinal))
            {
                continue;
            }
            var matchedRecordingToken = recordingToken!;
            var sourceValues = information.Descendants()
                .Where(item => item.Name.LocalName is "SourceToken" or "SourceId")
                .Select(item => item.Value.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            if (sourceValues.Length > 0 && !sourceValues.Contains(expectedSourceToken, StringComparer.Ordinal))
            {
                continue;
            }

            var tracks = information.Descendants().Where(item => item.Name.LocalName == "Track").ToList();
            if (tracks.Count == 0)
            {
                var segment = CreateSegment(information, matchedRecordingToken, "video", "video");
                if (segment is not null)
                {
                    yield return segment;
                }
                continue;
            }

            foreach (var track in tracks)
            {
                var segment = CreateSegment(track, matchedRecordingToken, ReadChildValue(track, "TrackToken") ?? "video", ReadChildValue(track, "TrackType") ?? "video");
                if (segment is not null)
                {
                    yield return segment;
                }
            }
        }
    }

    private static OnvifProfileGSegment? CreateSegment(XElement element, string recordingToken, string trackToken, string trackType)
    {
        var startedAt = ParseDateTimeOffset(ReadChildValue(element, "DataFrom"));
        var endedAt = ParseDateTimeOffset(ReadChildValue(element, "DataTo"));
        if (startedAt is null || endedAt is null || string.IsNullOrWhiteSpace(trackToken) || trackToken.Length > 512)
        {
            return null;
        }
        return new OnvifProfileGSegment(recordingToken, trackToken, trackType, startedAt.Value, endedAt.Value, true);
    }

    private static string FindUniqueRecordingToken(IReadOnlyList<OnvifRecordingDescriptor> recordings, string sourceToken)
    {
        var matches = recordings.Where(item => item.SourceIdentifiers.Contains(sourceToken, StringComparer.Ordinal)).ToList();
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(matches.Count == 0
                ? "ONVIF Profile G 没有返回与摄像头源令牌一致的录像令牌。 "
                : "ONVIF Profile G 返回多个匹配的录像令牌，拒绝猜测映射。 ");
        }
        return matches[0].RecordingToken!;
    }

    private static string GetSourceToken(WorkerCameraRoute camera)
    {
        try
        {
            using var document = JsonDocument.Parse(camera.StreamingChannelMap, JsonOptions);
            if (!document.RootElement.TryGetProperty("onvifSource", out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 512)
            {
                throw new InvalidOperationException("摄像头没有受控 ONVIF 源令牌。 ");
            }
            return value.GetString()!;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("摄像头 ONVIF 码流映射格式无效。 ", exception);
        }
    }

    private HttpClient CreateClient(NetworkCredential credential, WorkerRecorderEndpoint endpoint)
    {
        DeviceCertificatePolicy.EnsureTlsConfiguration(endpoint, options.ProfileG.AllowUntrustedCertificate);
        if (messageHandler is not null)
        {
            return new HttpClient(messageHandler, disposeHandler: false) { Timeout = options.ProfileG.RequestTimeout };
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
                    options.ProfileG.AllowUntrustedCertificate)
                : null
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = options.ProfileG.RequestTimeout };
    }

    private static async Task<IReadOnlyList<OnvifService>> GetServicesAsync(HttpClient client, Uri deviceService, CancellationToken cancellationToken)
    {
        const string payload = "<tds:GetServices xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"><tds:IncludeCapability>false</tds:IncludeCapability></tds:GetServices>";
        var document = await SendSoapAsync(client, deviceService, DeviceNamespace + "/GetServices", payload, cancellationToken);
        return document.Descendants().Where(item => item.Name.LocalName == "Service")
            .Select(item => new OnvifService(ReadChildValue(item, "Namespace"), ReadChildValue(item, "XAddr")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Namespace) && !string.IsNullOrWhiteSpace(item.XAddr))
            .ToList();
    }

    private static Uri GetProfileGServiceUri(IReadOnlyList<OnvifService> services, string serviceNamespace, Uri deviceService)
    {
        var service = services.SingleOrDefault(item => string.Equals(item.Namespace, serviceNamespace, StringComparison.Ordinal));
        if (service is null || !Uri.TryCreate(service.XAddr, UriKind.Absolute, out var serviceUri) || !IsSafeServiceUri(serviceUri, deviceService))
        {
            throw new InvalidOperationException("ONVIF Profile G 服务地址无效或越过已登记设备端点。 ");
        }
        return serviceUri;
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
            throw new HttpRequestException($"ONVIF Profile G 请求失败，状态码：{(int)response.StatusCode}。", null, response.StatusCode);
        }
        try
        {
            using var reader = XmlReader.Create(new StringReader(responseXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Descendants().Any(item => item.Name.LocalName == "Fault"))
            {
                throw new InvalidOperationException("ONVIF Profile G 设备返回 SOAP Fault。 ");
            }
            return document;
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("ONVIF Profile G 响应 XML 无效。 ", exception);
        }
    }

    private NetworkCredential GetCredential(WorkerRecorderAssignment assignment, string reference)
    {
        var credential = assignment.Credentials.SingleOrDefault(item => item.Name.Equals(reference, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("录像机分配缺少 ONVIF 凭据。 ");
        return credentialResolver.Resolve(credential);
    }

    private static WorkerRecorderEndpoint GetEndpoint(WorkerRecorderAssignment assignment, string protocol) =>
        assignment.Endpoints.SingleOrDefault(item => item.Protocol.Equals(protocol, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"录像机分配缺少 {protocol} 端点。 ");

    private static Uri CreateDeviceServiceUri(WorkerRecorderEndpoint endpoint)
    {
        if (!RecorderEndpointHostPolicy.IsValidHost(endpoint.Host) || endpoint.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("ONVIF 端点无效。 ");
        }
        return new UriBuilder(endpoint.UseTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, endpoint.Host, endpoint.Port, "/onvif/device_service").Uri;
    }

    private static bool IsSafeServiceUri(Uri serviceUri, Uri deviceService) =>
        (serviceUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || serviceUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
        serviceUri.Scheme.Equals(deviceService.Scheme, StringComparison.OrdinalIgnoreCase) &&
        serviceUri.Host.Equals(deviceService.Host, StringComparison.OrdinalIgnoreCase) &&
        serviceUri.Port == deviceService.Port &&
        string.IsNullOrEmpty(serviceUri.UserInfo);

    private static void ValidateReplayUri(Uri replayUri, WorkerRecorderEndpoint rtspEndpoint)
    {
        if ((!replayUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
             !replayUri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase)) ||
            !RecorderEndpointHostPolicy.IsValidHost(rtspEndpoint.Host) || rtspEndpoint.Port is < 1 or > 65535 ||
            !replayUri.Host.Equals(rtspEndpoint.Host, StringComparison.OrdinalIgnoreCase) || replayUri.Port != rtspEndpoint.Port ||
            !string.IsNullOrEmpty(replayUri.UserInfo))
        {
            throw new InvalidOperationException("ONVIF Profile G Replay URI 越过已登记 RTSP 端点或包含凭据。 ");
        }
    }

    private static string? ReadChildValue(XElement element, string localName) =>
        element.Elements().FirstOrDefault(item => item.Name.LocalName == localName)?.Value;

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string EscapeRequired(string value, string label) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 512
            ? SecurityElement.Escape(value) ?? throw new InvalidOperationException($"ONVIF {label} 无效。 ")
            : throw new InvalidOperationException($"ONVIF {label} 无效。 ");

    private static void ValidateSearchPayload(RecordingSearchCommandPayload payload, Guid expectedCameraId)
    {
        if (payload.CameraId != expectedCameraId || payload.StartedAt >= payload.EndedAt ||
            payload.EndedAt - payload.StartedAt > TimeSpan.FromDays(31))
        {
            throw new InvalidOperationException("ONVIF Profile G 检索命令参数无效。 ");
        }
    }

    private sealed record OnvifProfileGContext(HttpClient Client, Uri DeviceService);
    private sealed record OnvifService(string? Namespace, string? XAddr);
    private sealed record OnvifRecordingDescriptor(string? RecordingToken, IReadOnlyList<string> SourceIdentifiers);
}

public sealed record OnvifProfileGProbeResult(string RecordingToken, string SourceToken);

public sealed record OnvifProfileGReplaySource(Uri ReplayUri, string Username, string Password);

public sealed record OnvifProfileGSearchResult(
    Guid CameraId,
    string RecordingToken,
    string SourceToken,
    IReadOnlyList<OnvifProfileGSegment> Segments);

public sealed record OnvifProfileGSegment(
    string RecordingToken,
    string TrackToken,
    string TrackType,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    bool CoverageApproximate);
