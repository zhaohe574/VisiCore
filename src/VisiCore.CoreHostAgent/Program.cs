using VisiCore.CoreHostAgent;

var builder = Host.CreateApplicationBuilder(args);
var configurationPath = Environment.GetEnvironmentVariable("VISICORE_CORE_HOST_AGENT_CONFIG")
    ?? "/etc/visicore/core-host-agent.json";
if (File.Exists(configurationPath))
{
    builder.Configuration.AddJsonFile(configurationPath, optional: false, reloadOnChange: true);
}
var options = builder.Configuration.GetSection("CoreHostAgent").Get<CoreHostAgentOptions>() ?? new CoreHostAgentOptions();
builder.Services.AddSingleton(options);
builder.Services.AddHostedService<CoreHostAgentWorker>();

await builder.Build().RunAsync();
