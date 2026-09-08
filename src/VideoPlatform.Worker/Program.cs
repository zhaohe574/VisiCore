using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoPlatform.Infrastructure;
using VideoPlatform.Infrastructure.Jobs;
using VideoPlatform.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddPlatform(builder.Configuration);
builder.Services.AddPlatformJobs(builder.Configuration);
builder.Services.AddHostedService<PlatformWorker>();
using var host = builder.Build();
await Bootstrap.InitializeAsync(host.Services);
await host.RunAsync();
