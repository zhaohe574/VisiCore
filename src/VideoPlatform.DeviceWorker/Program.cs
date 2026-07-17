using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VideoPlatform.DeviceWorker;

if (args.Length == 1 && args[0].Equals("protect-credential", StringComparison.OrdinalIgnoreCase))
{
    Console.Write("设备用户名：");
    var username = Console.ReadLine() ?? string.Empty;
    Console.Write("设备密码（输入不回显）：");
    var password = ReadPassword();
    Console.WriteLine();
    Console.WriteLine(DeviceCredentialPayload.ProtectForCurrentMachine(username, password));
    return;
}

var builder = Host.CreateApplicationBuilder(args);
if (OperatingSystem.IsWindows())
{
    builder.Services.AddWindowsService(options => options.ServiceName = "VideoPlatform Device Worker");
}
builder.Services.Configure<DeviceWorkerOptions>(builder.Configuration.GetSection("DeviceWorker"));
builder.Services.Configure<ControlPlaneOptions>(builder.Configuration.GetSection("ControlPlane"));
builder.Services.Configure<OnvifReadOnlyOptions>(builder.Configuration.GetSection("Onvif"));
var controlPlaneOptions = builder.Configuration.GetSection("ControlPlane").Get<ControlPlaneOptions>() ?? new ControlPlaneOptions();
if (!controlPlaneOptions.TryGetBaseUri(out var controlPlaneBaseUri, out var controlPlaneValidationError))
{
    throw new InvalidOperationException(controlPlaneValidationError);
}

builder.Services.AddSingleton<WorkerCredentialStore>();
builder.Services.AddSingleton<IRecorderCredentialResolver, DpapiCredentialResolver>();
builder.Services.AddSingleton<OnvifReadOnlyClient>(services => new OnvifReadOnlyClient(
    services.GetRequiredService<IRecorderCredentialResolver>(),
    services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OnvifReadOnlyOptions>>().Value));
builder.Services.AddSingleton<OnvifDeviceCollector>();
builder.Services.AddSingleton<IRecorderInventoryCollector>(services => services.GetRequiredService<OnvifDeviceCollector>());
builder.Services.AddSingleton<DirectRtspDeviceCollector>();
builder.Services.AddSingleton<IRecorderInventoryCollector>(services => services.GetRequiredService<DirectRtspDeviceCollector>());
builder.Services.AddSingleton<RecorderInventoryCollectorRegistry>();
builder.Services.AddHttpClient<DeviceWorkerControlPlaneClient>(client => client.BaseAddress = controlPlaneBaseUri);
builder.Services.AddHostedService<RecorderInventoryWorker>();

await builder.Build().RunAsync();

static string ReadPassword()
{
    var buffer = new List<char>();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            if (buffer.Count > 0)
            {
                buffer.RemoveAt(buffer.Count - 1);
            }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            buffer.Add(key.KeyChar);
        }
    }
    return new string(buffer.ToArray());
}
