using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

[CollectionDefinition(nameof(WpfTestCollection), DisableParallelization = true)]
public sealed class WpfTestCollection : ICollectionFixture<WpfTestHost> { }

public sealed class WpfTestHost : IAsyncLifetime
{
    private readonly TaskCompletionSource<App> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    public bool StartupObserved { get; private set; }

    public WpfTestHost()
    {
        // WPF 在同一进程只能创建一个 Application，所有界面测试共用此 STA 线程。
        _thread = new Thread(Run) { IsBackground = true, Name = "WPF 测试宿主" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public async Task InitializeAsync()
    {
        _thread.Start();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async Task RunAsync(Func<Task> action, TimeSpan? timeout = null)
    {
        var app = await _ready.Task;
        await app.Dispatcher.InvokeAsync(action).Task.Unwrap().WaitAsync(timeout ?? TimeSpan.FromSeconds(30));
    }

    public async Task DisposeAsync()
    {
        if (_ready.Task.IsCompletedSuccessfully && !_stopped.Task.IsCompleted)
        {
            var app = _ready.Task.Result;
            _ = app.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(app.Shutdown));
        }
        await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(_thread.Join(TimeSpan.FromSeconds(5)), "WPF 测试线程未按时退出。");
    }

    private void Run()
    {
        try
        {
            var app = new App(startDesktop: false) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += (_, _) => StartupObserved = true;
            app.InitializeComponent();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
            // 等待真正的启动回调执行后才允许测试进入，避免只因未处理启动队列而误判隔离成功。
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => _ready.TrySetResult(app)));
            app.Run();
        }
        catch (Exception ex)
        {
            _ready.TrySetException(ex);
            _stopped.TrySetException(ex);
        }
        finally { _stopped.TrySetResult(); }
    }
}
