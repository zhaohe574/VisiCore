using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VisiCore.NotificationWorker;
using VisiCore.Persistence;

var builder = Host.CreateApplicationBuilder(args);
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "VisiCoreNotificationWorker");
}
var connectionString = builder.Configuration.GetConnectionString("Platform");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("必须配置 ConnectionStrings:Platform。 ");
}

builder.Services.Configure<NotificationWorkerOptions>(builder.Configuration.GetSection("NotificationWorker"));
var notificationOptions = builder.Configuration.GetSection("NotificationWorker").Get<NotificationWorkerOptions>() ?? new NotificationWorkerOptions();
notificationOptions.Validate();
builder.Services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<NotificationSecretResolver>();
builder.Services.AddSingleton<NotificationWebhookAddressProtector>();
builder.Services.AddHttpClient<NotificationDispatcher>(client => client.Timeout = TimeSpan.FromSeconds(notificationOptions.DeliveryTimeoutSeconds));
builder.Services.AddScoped<AlertEventProcessor>();
builder.Services.AddScoped<NotificationChannelTestEventProcessor>();
builder.Services.AddHostedService<NotificationProcessingWorker>();

await builder.Build().RunAsync();
