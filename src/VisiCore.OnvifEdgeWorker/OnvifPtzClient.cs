using System.Net;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using VisiCore.Core;

namespace VisiCore.OnvifEdgeWorker;

public interface IOnvifPtzClient
{
    Task ExecuteAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        PtzAction action,
        PtzMotion motion,
        int speed,
        CancellationToken cancellationToken);
}

public sealed class OnvifPtzClient(
    IOnvifEdgeCredentialResolver credentialResolver,
    OnvifEdgeOptions options,
    HttpMessageHandler? messageHandler = null) : IOnvifPtzClient
{
    private const string DeviceNamespace = "http://www.onvif.org/ver10/device/wsdl";
    private const string PtzNamespace = "http://www.onvif.org/ver20/ptz/wsdl";
    private const string SchemaNamespace = "http://www.onvif.org/ver10/schema";

    public async Task ExecuteAsync(
        WorkerRecorderAssignment assignment,
        WorkerCameraRoute camera,
        PtzAction action,
        PtzMotion motion,
        int speed,
        CancellationToken cancellationToken)
    {
        if (camera.CameraId == Guid.Empty || speed is < 1 or > 7 || !Enum.IsDefined(action) || !Enum.IsDefined(motion))
        {
            throw new InvalidOperationException("ONVIF PTZ 命令参数无效。 ");
        }
        var endpoint = GetEndpoint(assignment, "Onvif");
        var credential = GetCredential(assignment, endpoint.CredentialReference);
        using var client = CreateClient(credential, endpoint);
        var deviceService = CreateDeviceServiceUri(endpoint);
        var ptzService = await GetPtzServiceAsync(client, deviceService, cancellationToken);
        var profileToken = GetPtzProfileToken(camera);
        var payload = motion == PtzMotion.Start
            ? CreateContinuousMovePayload(profileToken, action, speed)
            : CreateStopPayload(profileToken, action);
        var operation = motion == PtzMotion.Start ? "ContinuousMove" : "Stop";
        await SendSoapAsync(client, ptzService, PtzNamespace + "/" + operation, payload, cancellationToken);
    }

    private HttpClient CreateClient(NetworkCredential credential, WorkerRecorderEndpoint endpoint)
    {
        DeviceCertificatePolicy.EnsureTlsConfiguration(endpoint, options.Ptz.AllowUntrustedCertificate);
        if (messageHandler is not null)
        {
            return new HttpClient(messageHandler, disposeHandler: false) { Timeout = options.Ptz.RequestTimeout };
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
                    options.Ptz.AllowUntrustedCertificate)
                : null
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = options.Ptz.RequestTimeout };
    }

    private static async Task<Uri> GetPtzServiceAsync(HttpClient client, Uri deviceService, CancellationToken cancellationToken)
    {
        const string payload = "<tds:GetServices xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"><tds:IncludeCapability>false</tds:IncludeCapability></tds:GetServices>";
        var document = await SendSoapAsync(client, deviceService, DeviceNamespace + "/GetServices", payload, cancellationToken);
        var service = document.Descendants().FirstOrDefault(item =>
            item.Name.LocalName == "Service" &&
            string.Equals(ReadChildValue(item, "Namespace"), PtzNamespace, StringComparison.Ordinal));
        var address = service is null ? null : ReadChildValue(service, "XAddr");
        if (!Uri.TryCreate(address, UriKind.Absolute, out var ptzService) || !IsSafeServiceUri(ptzService, deviceService))
        {
            throw new InvalidOperationException("ONVIF 设备未声明受限 PTZ 服务。 ");
        }
        return ptzService;
    }

    private static string GetPtzProfileToken(WorkerCameraRoute camera)
    {
        try
        {
            using var document = JsonDocument.Parse(camera.StreamingChannelMap);
            if (!document.RootElement.TryGetProperty("onvifPtzProfile", out var value) ||
                value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()) ||
                value.GetString()!.Length > 256)
            {
                throw new InvalidOperationException("摄像头未登记 ONVIF PTZ Profile。 ");
            }
            return value.GetString()!;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("摄像头 ONVIF 码流映射格式无效。 ", exception);
        }
    }

    private static string CreateContinuousMovePayload(string profileToken, PtzAction action, int speed)
    {
        var velocity = PtzVelocity.From(action, speed);
        var escapedToken = SecurityElement.Escape(profileToken) ?? throw new InvalidOperationException("ONVIF PTZ Profile 无效。 ");
        var panTilt = velocity.HasPanTilt
            ? $"<tt:PanTilt x=\"{velocity.Pan.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" y=\"{velocity.Tilt.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"/>"
            : string.Empty;
        var zoom = velocity.HasZoom
            ? $"<tt:Zoom x=\"{velocity.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"/>"
            : string.Empty;
        return $"<tptz:ContinuousMove xmlns:tptz=\"{PtzNamespace}\" xmlns:tt=\"{SchemaNamespace}\"><tptz:ProfileToken>{escapedToken}</tptz:ProfileToken><tptz:Velocity>{panTilt}{zoom}</tptz:Velocity></tptz:ContinuousMove>";
    }

    private static string CreateStopPayload(string profileToken, PtzAction action)
    {
        var escapedToken = SecurityElement.Escape(profileToken) ?? throw new InvalidOperationException("ONVIF PTZ Profile 无效。 ");
        var hasPanTilt = action is PtzAction.PanLeft or PtzAction.PanRight or PtzAction.TiltUp or PtzAction.TiltDown or
            PtzAction.PanTiltUpLeft or PtzAction.PanTiltUpRight or PtzAction.PanTiltDownLeft or PtzAction.PanTiltDownRight;
        var hasZoom = action is PtzAction.ZoomIn or PtzAction.ZoomOut;
        if (!hasPanTilt && !hasZoom)
        {
            throw new NotSupportedException("ONVIF 连续 PTZ 不支持焦距或光圈动作。 ");
        }
        return $"<tptz:Stop xmlns:tptz=\"{PtzNamespace}\"><tptz:ProfileToken>{escapedToken}</tptz:ProfileToken><tptz:PanTilt>{hasPanTilt.ToString().ToLowerInvariant()}</tptz:PanTilt><tptz:Zoom>{hasZoom.ToString().ToLowerInvariant()}</tptz:Zoom></tptz:Stop>";
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
            throw new HttpRequestException($"ONVIF PTZ 请求失败，状态码：{(int)response.StatusCode}。", null, response.StatusCode);
        }
        try
        {
            using var reader = XmlReader.Create(new StringReader(responseXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (document.Descendants().Any(item => item.Name.LocalName == "Fault"))
            {
                throw new InvalidOperationException("ONVIF PTZ 设备返回 SOAP Fault。 ");
            }
            return document;
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("ONVIF PTZ 响应 XML 无效。 ", exception);
        }
    }

    private static WorkerRecorderEndpoint GetEndpoint(WorkerRecorderAssignment assignment, string protocol) =>
        assignment.Endpoints.SingleOrDefault(item => item.Protocol.Equals(protocol, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"录像机分配缺少 {protocol} 端点。 ");

    private NetworkCredential GetCredential(WorkerRecorderAssignment assignment, string reference)
    {
        var credential = assignment.Credentials.SingleOrDefault(item => item.Name.Equals(reference, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("录像机分配缺少 ONVIF 凭据。 ");
        return credentialResolver.Resolve(credential);
    }

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

    private static string? ReadChildValue(XElement element, string localName) =>
        element.Elements().FirstOrDefault(item => item.Name.LocalName == localName)?.Value;

    private readonly record struct PtzVelocity(double Pan, double Tilt, double Zoom, bool HasPanTilt, bool HasZoom)
    {
        public static PtzVelocity From(PtzAction action, int speed)
        {
            var value = Math.Round(speed / 7d, 3, MidpointRounding.AwayFromZero);
            return action switch
            {
                PtzAction.PanLeft => new(-value, 0, 0, true, false),
                PtzAction.PanRight => new(value, 0, 0, true, false),
                PtzAction.TiltUp => new(0, value, 0, true, false),
                PtzAction.TiltDown => new(0, -value, 0, true, false),
                PtzAction.PanTiltUpLeft => new(-value, value, 0, true, false),
                PtzAction.PanTiltUpRight => new(value, value, 0, true, false),
                PtzAction.PanTiltDownLeft => new(-value, -value, 0, true, false),
                PtzAction.PanTiltDownRight => new(value, -value, 0, true, false),
                PtzAction.ZoomIn => new(0, 0, value, false, true),
                PtzAction.ZoomOut => new(0, 0, -value, false, true),
                _ => throw new NotSupportedException("ONVIF 连续 PTZ 不支持焦距或光圈动作。 ")
            };
        }
    }
}
