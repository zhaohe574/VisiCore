using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

[Collection(nameof(WpfTestCollection))]
public sealed class AppStartupIsolationTests(WpfTestHost host)
{
    [Fact]
    public Task StartupLoadsProductionResourcesWithoutOpeningMainWindow() => host.RunAsync(async () =>
    {
        var app = Assert.IsType<App>(Application.Current);
        Assert.True(host.StartupObserved);
        Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        Assert.IsType<DispatcherSynchronizationContext>(SynchronizationContext.Current);
        Assert.IsType<SolidColorBrush>(app.FindResource("PageBrush"));
        Assert.IsType<Style>(app.FindResource("IconButton"));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Null(app.MainWindow);
        Assert.Empty(app.Windows.Cast<Window>());
    });

    [Fact]
    public async Task DispatcherFailuresReachTestAndHostCanBeReused()
    {
        var failure = new InvalidOperationException("测试异常必须回传，不能弹出 MessageBox。");
        var synchronous = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(() => throw failure));
        Assert.Same(failure, synchronous);
        var asynchronous = await Assert.ThrowsAsync<InvalidOperationException>(() => host.RunAsync(async () =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            throw failure;
        }));
        Assert.Same(failure, asynchronous);
        await host.RunAsync(() =>
        {
            Assert.False(Application.Current.Dispatcher.HasShutdownStarted);
            return Task.CompletedTask;
        });
    }
}
