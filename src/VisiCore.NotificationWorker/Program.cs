using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisiCore.Persistence;

namespace VisiCore.NotificationWorker;

public static class NotificationWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddVisiCoreNotificationWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.Configure<NotificationWorkerOptions>(configuration.GetSection("NotificationWorker"));
        var options = configuration.GetSection("NotificationWorker").Get<NotificationWorkerOptions>() ?? new NotificationWorkerOptions();
        options.Validate();
        services.AddSingleton<NotificationSecretResolver>();
        services.AddHttpClient<NotificationDispatcher>(client => client.Timeout = TimeSpan.FromSeconds(options.DeliveryTimeoutSeconds));
        services.AddScoped<AlertEventProcessor>();
        services.AddScoped<NotificationChannelTestEventProcessor>();
        services.AddHostedService<NotificationProcessingWorker>();
        return services;
    }
}
