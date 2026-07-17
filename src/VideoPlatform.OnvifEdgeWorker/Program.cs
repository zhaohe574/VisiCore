using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoPlatform.OnvifEdgeWorker;

var builder = Host.CreateApplicationBuilder(args);
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "VideoPlatformOnvifEdgeWorker");
}

var options = builder.Configuration.GetSection("OnvifEdge").Get<OnvifEdgeOptions>() ?? new OnvifEdgeOptions();
var centerBaseUri = options.ValidateAndGetCenterBaseUri();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<OnvifEdgeCredentialResolver>();
builder.Services.AddSingleton<IOnvifEdgeCredentialResolver>(services => services.GetRequiredService<OnvifEdgeCredentialResolver>());
builder.Services.AddSingleton<IOnvifProfileGClient, OnvifProfileGClient>();
builder.Services.AddSingleton<OnvifProfileGCommandExecutor>();
builder.Services.AddSingleton<IOnvifFfmpegPlaybackRelay, FfmpegOnvifPlaybackRelay>();
builder.Services.AddSingleton<IOnvifPlaybackRelayManager, OnvifPlaybackRelayManager>();
builder.Services.AddSingleton<OnvifPlaybackRelayCommandExecutor>();
builder.Services.AddSingleton<OnvifOperationReadinessValidator>();
builder.Services.AddSingleton<IOnvifPtzClient, OnvifPtzClient>();
builder.Services.AddSingleton<OnvifPtzWatchdog>();
builder.Services.AddSingleton<OnvifPtzCommandExecutor>();
builder.Services.AddHttpClient<OnvifEdgeControlPlaneClient>(client =>
{
    client.BaseAddress = centerBaseUri;
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddSingleton<IOnvifPlaybackRelayAuthorization, OnvifPlaybackRelayAuthorization>();
builder.Services.AddHostedService<OnvifEdgeOperationStatusReporter>();
builder.Services.AddHostedService<OnvifEdgeCommandWorker>();

await builder.Build().RunAsync();
