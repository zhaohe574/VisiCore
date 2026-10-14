using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using VideoPlatform.Desktop.Views;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

[Collection(nameof(WpfTestCollection))]
public sealed class VisualSmokeTests(WpfTestHost host)
{
    [Fact]
    public async Task ViewsRenderAtCommonResolutionsAndScalesWithoutBindingErrors()
    {
        await host.RunAsync(async () =>
        {
            using var bindingLog = new BindingLog();
            var bindingSource = PresentationTraceSources.DataBindingSource;
            var previousLevel = bindingSource.Switch.Level;
            bindingSource.Listeners.Add(bindingLog);
            bindingSource.Switch.Level = SourceLevels.Error;
            try
            {
                var api = new FakeApi(); var player = new FakePlayerFactory(); var dispatcher = new InlineDispatcher(); var dialogs = new FakeDialogs();
                await using var ptz = new PtzService(api);
                var workspace = new WorkspaceViewModel(api, player, ptz, dispatcher, dialogs);
                await workspace.SetAccessAsync(Fixtures.User("live.view", "playback.view", "ptz.control", "channel.read", "export.create", "layout.share"));
                var device = new ResourceNode("视枢园区录像机"); var unit = new ResourceNode("生产一部");
                for (var i = 1; i <= 12; ++i) unit.Children.Add(new($"生产车间 {i:00} · 在线", Fixtures.Channel(i)));
                device.Children.Add(unit); workspace.Resources.Add(device);
                workspace.Layouts.Add(new(1, "厂区日间轮巡", "patrol", true, 4, 30, [1, 2, 3, 4]));
                workspace.RangeStart = DateTimeOffset.Now.AddHours(-1); workspace.RangeEnd = DateTimeOffset.Now; workspace.Playhead = workspace.RangeStart.AddMinutes(25);
                workspace.TimelineSegments = [new(workspace.RangeStart, workspace.RangeStart.AddMinutes(20)), new(workspace.RangeStart.AddMinutes(25), workspace.RangeEnd)];
                var alarms = new AlarmsViewModel(api); alarms.SetAccess(Fixtures.User("alarm.read", "alarm.ack"));
                for (var i = 1; i <= 8; ++i) alarms.Items.Add(new(i, 1, "视枢园区录像机", i, $"生产车间 {i:00}", "移动侦测", DateTimeOffset.Now.AddMinutes(-i), "new", false, null, null, "", false, null));
                var exports = new ExportsViewModel(api, dialogs); exports.SetAccess(Fixtures.User("export.create"));
                exports.Items.Add(new("task-1", 1, "生产车间 01", DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now, "running", 62, null, null, null, DateTimeOffset.Now, null));
                var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../screenshots")); Directory.CreateDirectory(output);
                foreach (var (width, height, scale) in new[] { (1366, 768, 1d), (1366, 768, 1.5), (1366, 768, 2d), (1920, 1080, 1d), (1920, 1080, 1.5), (1920, 1080, 2d) })
                {
                    var logicalWidth = width / scale; var logicalHeight = height / scale;
                    // 主预览按四等分渲染；同时覆盖 1+7 聚焦档位，确保大屏 + 小屏网格在不同 DPI 下不越界。
                    workspace.IsPlayback = false;
                    await RenderAsync(new WorkspaceView { DataContext = workspace }, logicalWidth, logicalHeight, scale, Path.Combine(output, $"live-{width}-{scale:0.0}.png"));
                    await workspace.SetLayoutCommand.ExecuteAsync("1+7");
                    await RenderAsync(new WorkspaceView { DataContext = workspace }, logicalWidth, logicalHeight, scale, Path.Combine(output, $"focus-{width}-{scale:0.0}.png"));
                    await workspace.SetLayoutCommand.ExecuteAsync("4");
                    workspace.IsPlayback = true;
                    await RenderAsync(new WorkspaceView { DataContext = workspace }, logicalWidth, logicalHeight, scale, Path.Combine(output, $"playback-{width}-{scale:0.0}.png"));
                    await RenderAsync(new AlarmsView { DataContext = alarms }, logicalWidth, logicalHeight, scale, Path.Combine(output, $"alarms-{width}-{scale:0.0}.png"));
                    await RenderAsync(new ExportsView { DataContext = exports }, logicalWidth, logicalHeight, scale, Path.Combine(output, $"exports-{width}-{scale:0.0}.png"));
                }
                Assert.Empty(bindingLog.Errors);
            }
            finally
            {
                bindingSource.Listeners.Remove(bindingLog);
                bindingSource.Switch.Level = previousLevel;
            }
        }, TimeSpan.FromSeconds(30));
    }
    private static async Task RenderAsync(FrameworkElement view, double width, double height, double scale, string path)
    {
        view.MinWidth = 1000; view.MinHeight = 650;
        var viewport = new ScrollViewer { Content = view, Width = width, Height = height, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var window = new Window { Content = viewport, Width = width, Height = height, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Show();
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        try
        {
        viewport.Measure(new Size(width, height)); viewport.Arrange(new Rect(0, 0, width, height)); viewport.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        // 数据绑定可能重新触发布局，截图前完成第二次布局，避免捕获尚未绘制的离屏树。
        viewport.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(viewport);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert.True(pixels.Where((_, i) => i % 4 != 3).Distinct().Count() > 15,
            $"界面渲染必须包含可见内容：{view.GetType().Name}，可见 {view.IsVisible}，尺寸 {view.ActualWidth}×{view.ActualHeight}，视口 {viewport.ActualWidth}×{viewport.ActualHeight}，子元素 {VisualTreeHelper.GetChildrenCount(view)}，颜色数 {pixels.Distinct().Count()}。");
        }
        finally { window.Content = null; window.Close(); }
    }
    private sealed class BindingLog : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
