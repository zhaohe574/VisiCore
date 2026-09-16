using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;

[assembly: InternalsVisibleTo("VideoPlatform.Desktop.Tests")]

namespace VideoPlatform.Desktop;

public partial class App : Application
{
    private readonly bool _startDesktop;
    private ServiceProvider? _services;

    public App() : this(startDesktop: true) { }

    internal App(bool startDesktop) => _startDesktop = startDesktop;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 测试仍加载正式资源并处理 WPF 启动队列，但不创建服务、主窗口或模态异常对话框。
        if (!_startDesktop) return;
        DispatcherUnhandledException += (_, args) =>
        {
            ClientFiles.Log($"界面操作异常：{args.Exception.Message}");
            MessageBox.Show($"操作未完成：{args.Exception.Message}", "VisiCore（视枢）", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AllowAutoRedirect = false,
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 100,
            ConnectCallback = async (context, token) =>
            {
                var socket = new System.Net.Sockets.Socket(System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
                if (context.DnsEndPoint.Host.StartsWith("10.37."))
                {
                    var local = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        .Where(i => i.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                        .SelectMany(i => i.GetIPProperties().UnicastAddresses)
                        .Select(a => a.Address)
                        .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && a.ToString().StartsWith("10.37."));
                    if (local is not null) socket.Bind(new System.Net.IPEndPoint(local, 0));
                }
                await socket.ConnectAsync(context.DnsEndPoint, token);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        }) { Timeout = TimeSpan.FromSeconds(60) });
        services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<IUserInteraction, UserInteraction>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<IPlatformApi, PlatformApi>();
        services.AddSingleton<IPlayerFactory, VlcPlayerFactory>();
        // 设置页需要按硬解/缓存参数重新配置同一个工厂实例。
        services.AddSingleton(sp => (VlcPlayerFactory)sp.GetRequiredService<IPlayerFactory>());
        services.AddSingleton<PtzService>();
        services.AddSingleton<EventService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<WorkspaceViewModel>();
        services.AddSingleton<AlarmsViewModel>();
        services.AddSingleton<ExportsViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();
        _services = services.BuildServiceProvider();
        MainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow.Show();
        StressMode.Attach(_services.GetRequiredService<ShellViewModel>());
    }
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_services is not null) await _services.DisposeAsync();
        base.OnExit(e);
    }
}
