using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting.WindowsServices;
using VisiCore.EdgeAgent;
using VisiCore.DeviceWorker;

var builder = WebApplication.CreateBuilder(args);
var managedConfigurationPath = Environment.GetEnvironmentVariable("VISICORE_EDGE_AGENT_CONFIG")
    ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeAgent", "edge-agent.json")
        : "/var/lib/visicore/edge-agent/edge-agent.json");
if (File.Exists(managedConfigurationPath))
{
    builder.Configuration.AddJsonFile(managedConfigurationPath, optional: false, reloadOnChange: true);
}
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "VisiCore Edge Agent");
}
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

var edgeAgentOptions = builder.Configuration.GetSection("EdgeAgent").Get<EdgeAgentOptions>() ?? new EdgeAgentOptions();
var hostAgentOptions = builder.Configuration.GetSection("HostAgent").Get<HostAgentOptions>() ?? new HostAgentOptions();
builder.Services.AddSingleton(edgeAgentOptions);
builder.Services.AddSingleton(hostAgentOptions);
builder.Services.AddSingleton<EdgeAgentRuntimeState>();
builder.Services.AddSingleton<EdgeAgentRuntimeSettings>();
builder.Services.AddSingleton<HostOperationState>();
builder.Services.AddSingleton<EdgeAgentIdentityStore>();
builder.Services.AddSingleton<EdgeAgentBootstrapStore>();
builder.Services.AddSingleton<HostOperationExchange>();
builder.Services.Configure<OnvifReadOnlyOptions>(builder.Configuration.GetSection("Onvif"));
var onvifOptions = builder.Configuration.GetSection("Onvif").Get<OnvifReadOnlyOptions>() ?? new OnvifReadOnlyOptions();
if (!onvifOptions.TryValidate(out var onvifValidationError))
{
    throw new InvalidOperationException(onvifValidationError);
}
builder.Services.AddSingleton(onvifOptions);
builder.Services.AddSingleton<EdgeAgentCredentialResolver>();
builder.Services.AddSingleton<IRecorderCredentialResolver>(provider => provider.GetRequiredService<EdgeAgentCredentialResolver>());
builder.Services.AddSingleton<OnvifReadOnlyClient>(provider => new OnvifReadOnlyClient(
    provider.GetRequiredService<IRecorderCredentialResolver>(),
    provider.GetRequiredService<OnvifReadOnlyOptions>()));
builder.Services.AddSingleton<OnvifDeviceCollector>();
builder.Services.AddSingleton<DirectRtspDeviceCollector>();
builder.Services.AddSingleton<EdgeAgentDeviceSynchronizer>();

if (edgeAgentOptions.TryGetControlPlaneBaseUri(out var controlPlaneBaseUri, out _))
{
    builder.Services.AddHttpClient<EdgeAgentControlPlaneClient>(client =>
    {
        client.BaseAddress = controlPlaneBaseUri;
        client.Timeout = TimeSpan.FromSeconds(15);
    });
}
else
{
    // 保持存活和本地状态端点可用，便于后台显示“待配置”，不伪造控制面连接。
    builder.Services.AddHttpClient<EdgeAgentControlPlaneClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
}

builder.Services.AddHostedService<EdgeAgentRuntimeWorker>();
// 宿主升级器必须独立运行，业务 Agent 不取得 Docker、shell 或系统写入权限。
builder.Services.AddHealthChecks()
    .AddCheck<EdgeAgentLivenessHealthCheck>("edge_agent_liveness", HealthStatus.Unhealthy)
    .AddCheck<EdgeAgentReadinessHealthCheck>("edge_agent_control_plane", HealthStatus.Degraded);

var app = builder.Build();
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = registration => registration.Name == "edge_agent_liveness"
});
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = _ => true });
app.MapGet("/api/v1/edge-agent/identity", (
    EdgeAgentOptions options,
    EdgeAgentRuntimeState runtimeState) =>
{
    var snapshot = runtimeState.Snapshot();
    return Results.Ok(new
    {
        snapshot.AgentId,
        snapshot.KeyId,
        isEnrolled = snapshot.LastHeartbeatAt is not null,
        agentVersion = options.GetAgentVersion(),
        platform = options.GetPlatform(),
        capabilities = options.Capabilities
    });
});
app.MapGet("/api/v1/edge-agent/runtime", (
    EdgeAgentRuntimeState runtimeState,
    HostOperationState hostOperationState) => Results.Ok(new
{
    runtime = runtimeState.Snapshot(),
    hostAgent = hostOperationState.Snapshot()
}));

await app.RunAsync();
