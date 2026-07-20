using Microsoft.Extensions.Hosting.WindowsServices;
using VisiCore.EdgeAgent;

var builder = Host.CreateApplicationBuilder(args);
var managedConfigurationPath = Environment.GetEnvironmentVariable("VISICORE_EDGE_HOST_AGENT_CONFIG")
    ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "VisiCore", "EdgeHostAgent", "edge-host-agent.json")
        : "/etc/visicore/edge-host-agent/edge-host-agent.json");
if (File.Exists(managedConfigurationPath))
{
    builder.Configuration.AddJsonFile(managedConfigurationPath, optional: false, reloadOnChange: true);
}
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "VisiCore Edge Host Agent");
}

var options = builder.Configuration.GetSection("HostAgent").Get<HostAgentOptions>() ?? new HostAgentOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<HostOperationState>();
builder.Services.AddSingleton<HostOperationExchange>();
builder.Services.AddSingleton<VerifiedDeploymentStore>();
builder.Services.AddSingleton<HostReleaseArtifactVerifier>();
builder.Services.AddSingleton<DockerComposeHostOperationExecutor>();
builder.Services.AddHostedService<HostConfigurationSocketWorker>();
builder.Services.AddSingleton<WindowsMsiHostOperationExecutor>();
builder.Services.AddSingleton<IHostOperationExecutor, PlatformHostOperationExecutor>();
builder.Services.AddSingleton<HostOperationWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<HostOperationWorker>());

await builder.Build().RunAsync();
