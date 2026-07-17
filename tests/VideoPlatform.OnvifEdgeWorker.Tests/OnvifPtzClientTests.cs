using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using VideoPlatform.Core;
using VideoPlatform.OnvifEdgeWorker;
using Xunit;

namespace VideoPlatform.OnvifEdgeWorker.Tests;

public sealed class OnvifPtzClientTests
{
    [Fact(DisplayName = "ONVIF 连续移动使用受限 PTZ 服务和 Profile token")]
    public async Task ContinuousMoveUsesMappedProfileAndVelocity()
    {
        var requests = new List<string>();
        var client = CreateClient(requests, externalService: false);

        await client.ExecuteAsync(CreateAssignment(), CreateCamera(), PtzAction.PanLeft, PtzMotion.Start, 7, CancellationToken.None);

        Assert.Equal(2, requests.Count);
        Assert.Contains("GetServices", requests[0]);
        Assert.Contains("ContinuousMove", requests[1]);
        Assert.Contains("ptz-profile", requests[1]);
        Assert.Contains("x=\"-1\"", requests[1]);
        Assert.Contains("y=\"0\"", requests[1]);
    }

    [Fact(DisplayName = "ONVIF STOP 仅停止当前变焦轴")]
    public async Task StopTargetsRequestedAxis()
    {
        var requests = new List<string>();
        var client = CreateClient(requests, externalService: false);

        await client.ExecuteAsync(CreateAssignment(), CreateCamera(), PtzAction.ZoomOut, PtzMotion.Stop, 1, CancellationToken.None);

        Assert.Contains("Stop", requests[1]);
        Assert.Contains("<tptz:PanTilt>false</tptz:PanTilt>", requests[1]);
        Assert.Contains("<tptz:Zoom>true</tptz:Zoom>", requests[1]);
    }

    [Fact(DisplayName = "ONVIF PTZ 服务跨越已登记主机时拒绝执行")]
    public async Task PtzServiceOnAnotherHostIsRejected()
    {
        var requests = new List<string>();
        var client = CreateClient(requests, externalService: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExecuteAsync(CreateAssignment(), CreateCamera(), PtzAction.PanRight, PtzMotion.Start, 1, CancellationToken.None));

        Assert.Contains("受限 PTZ 服务", exception.Message);
        Assert.Single(requests);
    }

    [Fact(DisplayName = "ONVIF PTZ Watchdog 的显式停止复用原始动作")]
    public async Task WatchdogStopsActiveMotion()
    {
        var client = new RecordingPtzClient();
        await using var watchdog = new OnvifPtzWatchdog(
            client,
            new OnvifEdgeOptions { Ptz = new OnvifPtzOptions { Enabled = true, MaxPulseMilliseconds = 250 } },
            NullLogger<OnvifPtzWatchdog>.Instance);
        var payload = new PtzControlCommandPayload(Guid.NewGuid(), CreateCamera().CameraId, PtzAction.TiltUp, PtzMotion.Start, 3, 1, 250);

        await watchdog.StartAsync(CreateAssignment(), CreateCamera(), payload, CancellationToken.None);
        Assert.True(await watchdog.StopActiveAsync(payload.CameraId, CancellationToken.None));

        Assert.Equal([(PtzMotion.Start, PtzAction.TiltUp), (PtzMotion.Stop, PtzAction.TiltUp)], client.Commands);
    }

    [Fact(DisplayName = "ONVIF PTZ 停止失败后禁止再次启动同一摄像头")]
    public async Task StopFailureBlocksFurtherStarts()
    {
        var client = new StopFailingPtzClient();
        await using var watchdog = new OnvifPtzWatchdog(
            client,
            new OnvifEdgeOptions { Ptz = new OnvifPtzOptions { Enabled = true, MaxPulseMilliseconds = 250 } },
            NullLogger<OnvifPtzWatchdog>.Instance);
        var camera = CreateCamera();
        var payload = new PtzControlCommandPayload(Guid.NewGuid(), camera.CameraId, PtzAction.PanLeft, PtzMotion.Start, 3, 1, 250);

        await watchdog.StartAsync(CreateAssignment(), camera, payload, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => watchdog.StopActiveAsync(camera.CameraId, CancellationToken.None));
        Assert.True(watchdog.HasUnconfirmedStop);
        await Assert.ThrowsAsync<InvalidOperationException>(() => watchdog.StartAsync(
            CreateAssignment(), camera, payload with { Sequence = 2 }, CancellationToken.None));
        Assert.Equal(1, client.StartCount);
    }

    private static OnvifPtzClient CreateClient(ICollection<string> requests, bool externalService) => new(
        new FixedCredentialResolver(),
        new OnvifEdgeOptions { Ptz = new OnvifPtzOptions { Enabled = true } },
        new DelegateHttpMessageHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            requests.Add(body);
            if (body.Contains("GetServices", StringComparison.Ordinal))
            {
                var host = externalService ? "outside.example" : "nvr.example";
                return SoapResponse($"<tds:GetServicesResponse xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\"><tds:Service><tds:Namespace>http://www.onvif.org/ver20/ptz/wsdl</tds:Namespace><tds:XAddr>http://{host}:80/onvif/ptz_service</tds:XAddr></tds:Service></tds:GetServicesResponse>");
            }
            return SoapResponse("<tptz:Response xmlns:tptz=\"http://www.onvif.org/ver20/ptz/wsdl\" />");
        }));

    private static WorkerRecorderAssignment CreateAssignment() => new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "ZNV-01",
        "通用 ONVIF 验证录像机",
        "Generic",
        "onvif-standard",
        "Asia/Shanghai",
        [new WorkerRecorderEndpoint("Onvif", "nvr.example", 80, false, "credential-a")],
        [new WorkerProtectedCredential("credential-a", "WindowsDpapiLocalMachine", "unused", "v1")],
        [CreateCamera()]);

    private static WorkerCameraRoute CreateCamera() => new(
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        1,
        "{\"main\":\"rtsp://nvr.example:554/live/main\",\"sub\":\"rtsp://nvr.example:554/live/sub\",\"onvifPtzProfile\":\"ptz-profile\"}");

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

    private sealed class RecordingPtzClient : IOnvifPtzClient
    {
        public List<(PtzMotion Motion, PtzAction Action)> Commands { get; } = [];

        public Task ExecuteAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, PtzAction action, PtzMotion motion, int speed, CancellationToken cancellationToken)
        {
            Commands.Add((motion, action));
            return Task.CompletedTask;
        }
    }

    private sealed class StopFailingPtzClient : IOnvifPtzClient
    {
        public int StartCount { get; private set; }

        public Task ExecuteAsync(WorkerRecorderAssignment assignment, WorkerCameraRoute camera, PtzAction action, PtzMotion motion, int speed, CancellationToken cancellationToken)
        {
            if (motion == PtzMotion.Stop)
            {
                throw new InvalidOperationException("模拟 PTZ 停止失败。");
            }
            StartCount++;
            return Task.CompletedTask;
        }
    }
}
