using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using VisiCore.EdgeAgent;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

var edgeAgentOptions = builder.Configuration.GetSection("EdgeAgent").Get<EdgeAgentOptions>() ?? new EdgeAgentOptions();
var hostAgentOptions = builder.Configuration.GetSection("HostAgent").Get<HostAgentOptions>() ?? new HostAgentOptions();
builder.Services.AddSingleton(edgeAgentOptions);
builder.Services.AddSingleton(hostAgentOptions);
builder.Services.AddSingleton<EdgeAgentRuntimeState>();
builder.Services.AddSingleton<HostOperationState>();
builder.Services.AddSingleton<EdgeAgentIdentityStore>();
builder.Services.AddSingleton<DockerComposeHostOperationExecutor>();
builder.Services.AddSingleton<VerifiedDeploymentStore>();
builder.Services.AddSingleton<HostOperationWorker>();

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
builder.Services.AddHostedService(provider => provider.GetRequiredService<HostOperationWorker>());
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
