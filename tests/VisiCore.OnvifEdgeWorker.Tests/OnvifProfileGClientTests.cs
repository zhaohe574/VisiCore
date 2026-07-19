using System.Net;
using VisiCore.Core;
using VisiCore.OnvifEdgeWorker;
using Xunit;

namespace VisiCore.OnvifEdgeWorker.Tests;

public sealed class OnvifProfileGClientTests
{
    [Fact(DisplayName = "Profile G 检索使用受限服务并在结束后释放搜索会话")]
    public async Task SearchReturnsApproximateCoverageAndEndsSearch()
    {
        var requests = new List<string>();
        var client = new OnvifProfileGClient(
            new FixedCredentialResolver(),
            EnabledOptions(),
            new DelegateHttpMessageHandler(async request =>
            {
                var body = await request.Content!.ReadAsStringAsync();
                if (body.Contains("GetServices", StringComparison.Ordinal))
                {
                    requests.Add("GetServices");
                    return SoapResponse("<tds:GetServicesResponse xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"><tds:Service><tds:Namespace>http://www.onvif.org/ver10/recording/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/recording_service</tds:XAddr></tds:Service><tds:Service><tds:Namespace>http://www.onvif.org/ver10/search/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/search_service</tds:XAddr></tds:Service><tds:Service><tds:Namespace>http://www.onvif.org/ver10/replay/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/replay_service</tds:XAddr></tds:Service></tds:GetServicesResponse>");
                }
                if (body.Contains("GetRecordings", StringComparison.Ordinal))
                {
                    requests.Add("GetRecordings");
                    return SoapResponse("<trc:GetRecordingsResponse xmlns:trc=\"http://www.onvif.org/ver10/recording/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><trc:RecordingItem><trc:RecordingToken>recording-north</trc:RecordingToken><trc:Configuration><tt:Source><tt:SourceId>source-north</tt:SourceId></tt:Source></trc:Configuration></trc:RecordingItem></trc:GetRecordingsResponse>");
                }
                if (body.Contains("FindRecordings", StringComparison.Ordinal))
                {
                    requests.Add("FindRecordings");
                    return SoapResponse("<tse:FindRecordingsResponse xmlns:tse=\"http://www.onvif.org/ver10/search/wsdl\"><tse:SearchToken>search-north</tse:SearchToken></tse:FindRecordingsResponse>");
                }
                if (body.Contains("GetRecordingSearchResults", StringComparison.Ordinal))
                {
                    requests.Add("GetRecordingSearchResults");
                    return SoapResponse("<tse:GetRecordingSearchResultsResponse xmlns:tse=\"http://www.onvif.org/ver10/search/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><tse:ResultList><tse:SearchState>Completed</tse:SearchState><tse:RecordingInformation><tt:RecordingToken>recording-north</tt:RecordingToken><tt:Source><tt:SourceId>source-north</tt:SourceId></tt:Source><tt:Track><tt:TrackToken>video-north</tt:TrackToken><tt:TrackType>Video</tt:TrackType><tt:DataFrom>2026-07-13T00:00:00Z</tt:DataFrom><tt:DataTo>2026-07-13T01:00:00Z</tt:DataTo></tt:Track></tse:RecordingInformation></tse:ResultList></tse:GetRecordingSearchResultsResponse>");
                }
                if (body.Contains("EndSearch", StringComparison.Ordinal))
                {
                    requests.Add("EndSearch");
                    return SoapResponse("<tse:EndSearchResponse xmlns:tse=\"http://www.onvif.org/ver10/search/wsdl\"/>");
                }
                throw new InvalidOperationException("收到未预期的 ONVIF Profile G 请求。 ");
            }));

        var result = await client.SearchAsync(
            CreateAssignment(),
            CreateCamera(),
            new RecordingSearchCommandPayload(CreateCamera().CameraId, DateTimeOffset.Parse("2026-07-13T00:00:00Z"), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 10),
            CancellationToken.None);

        var segment = Assert.Single(result.Segments);
        Assert.Equal("recording-north", result.RecordingToken);
        Assert.True(segment.CoverageApproximate);
        Assert.Equal("video-north", segment.TrackToken);
        Assert.Equal(["GetServices", "GetRecordings", "FindRecordings", "GetRecordingSearchResults", "EndSearch"], requests);
    }

    [Fact(DisplayName = "Profile G 服务越过登记主机时拒绝检索")]
    public async Task SearchRejectsProfileGServiceOnAnotherHost()
    {
        var client = new OnvifProfileGClient(
            new FixedCredentialResolver(),
            EnabledOptions(),
            new DelegateHttpMessageHandler(async request =>
            {
                var body = await request.Content!.ReadAsStringAsync();
                Assert.Contains("GetServices", body, StringComparison.Ordinal);
                return SoapResponse("<tds:GetServicesResponse xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"><tds:Service><tds:Namespace>http://www.onvif.org/ver10/recording/wsdl</tds:Namespace><tds:XAddr>http://outside.example/onvif/recording_service</tds:XAddr></tds:Service><tds:Service><tds:Namespace>http://www.onvif.org/ver10/search/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/search_service</tds:XAddr></tds:Service><tds:Service><tds:Namespace>http://www.onvif.org/ver10/replay/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/replay_service</tds:XAddr></tds:Service></tds:GetServicesResponse>");
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SearchAsync(
            CreateAssignment(),
            CreateCamera(),
            new RecordingSearchCommandPayload(CreateCamera().CameraId, DateTimeOffset.Parse("2026-07-13T00:00:00Z"), DateTimeOffset.Parse("2026-07-13T01:00:00Z"), 10),
            CancellationToken.None));

        Assert.Contains("越过", exception.Message);
    }

    [Fact(DisplayName = "Profile G Replay URI 必须受限于登记 RTSP 端点且不能携带凭据")]
    public async Task ReplayUriMustMatchRegisteredRtspEndpoint()
    {
        var client = new OnvifProfileGClient(
            new FixedCredentialResolver(),
            EnabledOptions(),
            new DelegateHttpMessageHandler(async request =>
            {
                var body = await request.Content!.ReadAsStringAsync();
                if (body.Contains("GetServices", StringComparison.Ordinal))
                {
                    return SoapResponse("<tds:GetServicesResponse xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"><tds:Service><tds:Namespace>http://www.onvif.org/ver10/recording/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/recording_service</tds:XAddr></tds:Service><tds:Service><tds:Namespace>http://www.onvif.org/ver10/search/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/search_service</tds:XAddr></tds:Service><tds:Service><tds:Namespace>http://www.onvif.org/ver10/replay/wsdl</tds:Namespace><tds:XAddr>http://nvr.example/onvif/replay_service</tds:XAddr></tds:Service></tds:GetServicesResponse>");
                }
                if (body.Contains("GetRecordings", StringComparison.Ordinal))
                {
                    return SoapResponse("<trc:GetRecordingsResponse xmlns:trc=\"http://www.onvif.org/ver10/recording/wsdl\" xmlns:tt=\"http://www.onvif.org/ver10/schema\"><trc:RecordingItem><trc:RecordingToken>recording-north</trc:RecordingToken><trc:Configuration><tt:Source><tt:SourceId>source-north</tt:SourceId></tt:Source></trc:Configuration></trc:RecordingItem></trc:GetRecordingsResponse>");
                }
                if (body.Contains("GetReplayUri", StringComparison.Ordinal))
                {
                    return SoapResponse("<trp:GetReplayUriResponse xmlns:trp=\"http://www.onvif.org/ver10/replay/wsdl\"><trp:Uri>rtsp://viewer:secret@outside.example:554/replay</trp:Uri></trp:GetReplayUriResponse>");
                }
                throw new InvalidOperationException("收到未预期的 ONVIF Profile G 请求。 ");
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetReplayUriAsync(CreateAssignment(), CreateCamera(), CancellationToken.None));

        Assert.Contains("Replay URI", exception.Message);
    }

    private static OnvifEdgeOptions EnabledOptions() => new()
    {
        ProfileG = new OnvifProfileGOptions
        {
            Enabled = true,
            RequestTimeout = TimeSpan.FromSeconds(10),
            MaxSearchResults = 50,
            MaxSearchPolls = 2
        }
    };

    private static WorkerRecorderAssignment CreateAssignment() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "ZNV-01",
        "通用 ONVIF 验证录像机",
        "Generic",
        "onvif-standard",
        "Asia/Shanghai",
        [
            new WorkerRecorderEndpoint("Onvif", "nvr.example", 80, false, "credential-a"),
            new WorkerRecorderEndpoint("Rtsp", "nvr.example", 554, false, "credential-a")
        ],
        [new WorkerProtectedCredential("credential-a", "WindowsDpapiLocalMachine", "unused", "v1")],
        [CreateCamera()]);

    private static WorkerCameraRoute CreateCamera() => new(
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        1,
        "{\"onvifSource\":\"source-north\",\"main\":\"rtsp://nvr.example:554/live/main\",\"sub\":\"rtsp://nvr.example:554/live/sub\"}");

    private static HttpResponseMessage SoapResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"<?xml version=\"1.0\" encoding=\"utf-8\"?><s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body>{body}</s:Body></s:Envelope>")
    };

    private sealed class FixedCredentialResolver : IOnvifEdgeCredentialResolver
    {
        public NetworkCredential Resolve(WorkerProtectedCredential credential) => new("viewer", "not-a-real-password");
    }

    private sealed class DelegateHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
