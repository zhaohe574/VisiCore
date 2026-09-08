using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;
using VideoPlatform.Desktop.Views;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

[Collection(nameof(WpfTestCollection))]
public sealed class MainWindowLayoutTests(WpfTestHost host)
{
    [Theory]
    [InlineData(4, false)]
    [InlineData(16, false)]
    [InlineData(4, true)]
    [InlineData(16, true)]
    public Task MainWindowViewportContainsEveryTileAndStatusBar(int count, bool playback) => host.RunAsync(async () =>
    {
        var api = new FakeApi();
        await using var ptz = new PtzService(api);
        var workspace = new WorkspaceViewModel(api, new FakePlayerFactory(), ptz, new InlineDispatcher(), new FakeDialogs()) { IsPlayback = playback };
        await workspace.SetLayoutCommand.ExecuteAsync(count.ToString());
        var window = LoadLayout(workspace, playback);
        try
        {
            var scroll = Assert.IsType<ScrollViewer>(window.Content);
            var layout = Assert.IsType<Grid>(scroll.Content);
            var status = Assert.Single(layout.Children.OfType<DockPanel>(), child => Grid.GetRow(child) == 3);
            await LayoutAsync(scroll, 1440, 900);
            var tiles = Descendants<VideoTileView>(scroll).Where(tile => tile.DataContext is VideoTileViewModel { IsVisible: true }).ToArray();
            Assert.Equal(count, tiles.Length);
            var probes = tiles.Select(tile =>
            {
                var probe = new VideoMeasureProbe();
                Assert.IsType<ContentControl>(tile.FindName("PlayerHost")).Content = probe;
                return probe;
            }).ToArray();

            // 始终从正式外层 ScrollViewer 测量；不向 Grid 或 Workspace 注入有限尺寸。
            foreach (var (width, height) in new[] { (1440d, 900d), (1200d, 760d), (1440d, 900d) })
            {
                await LayoutAsync(scroll, width, height);
                Assert.InRange(scroll.ScrollableWidth, 0, 0.5);
                Assert.InRange(scroll.ScrollableHeight, 0, 0.5);
                Assert.InRange(Math.Abs(layout.ActualWidth - scroll.ViewportWidth), 0, 0.5);
                Assert.InRange(Math.Abs(layout.ActualHeight - scroll.ViewportHeight), 0, 0.5);
                var viewport = new Rect(0, 0, scroll.ViewportWidth, scroll.ViewportHeight);
                AssertContained(status, scroll, viewport);
                var grid = Assert.Single(Descendants<System.Windows.Controls.Primitives.UniformGrid>(scroll),
                    panel => panel.Children.OfType<ContentPresenter>().Any(child => child.Content is VideoTileViewModel));
                foreach (var tile in tiles)
                {
                    AssertContained(tile, scroll, viewport);
                    AssertContained(tile, grid, new Rect(grid.RenderSize));
                    var playerHost = Assert.IsType<ContentControl>(tile.FindName("PlayerHost"));
                    AssertContained(playerHost, tile, new Rect(tile.RenderSize));
                    Assert.True(playerHost.ActualWidth > 0 && playerHost.ActualHeight > 0, "原生播放器宿主必须有有限且非空的画面区域。");
                    Assert.True(tile.ActualHeight > 40 && tile.ActualWidth > 40, "分屏必须有可用画面区域。");
                    Assert.InRange(Math.Abs(tile.ActualHeight - tiles[0].ActualHeight), 0, 1);
                }
                Assert.All(probes, probe => Assert.True(double.IsFinite(probe.Constraint.Width) && double.IsFinite(probe.Constraint.Height), "视频必须在有限空间中测量。"));
            }

            // 高 DPI 或窄窗口下保留最小逻辑工作区，并能滚动到末行和状态栏。
            await LayoutAsync(scroll, 800, 500);
            Assert.InRange(layout.ActualWidth, 1000, 1001);
            Assert.InRange(layout.ActualHeight, 650, 651);
            Assert.True(scroll.ScrollableWidth > 0 && scroll.ScrollableHeight > 0);
            scroll.ScrollToRightEnd();
            scroll.ScrollToBottom();
            await LayoutAsync(scroll, 800, 500);
            Assert.InRange(Math.Abs(scroll.HorizontalOffset - scroll.ScrollableWidth), 0, 1);
            Assert.InRange(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight), 0, 1);
            var statusBounds = status.TransformToAncestor(scroll).TransformBounds(new Rect(status.RenderSize));
            Assert.InRange(statusBounds.Bottom, scroll.ViewportHeight - 1, scroll.ViewportHeight + 1);
            var lastBounds = tiles[^1].TransformToAncestor(scroll).TransformBounds(new Rect(tiles[^1].RenderSize));
            Assert.True(lastBounds.Right <= scroll.ViewportWidth + 1 && lastBounds.Bottom <= scroll.ViewportHeight + 1, "滚动后最后一个分屏必须可达。");
            foreach (var (width, height) in new[] { (1000d, 650d), (1440d, 900d) })
            {
                await LayoutAsync(scroll, width, height);
                Assert.InRange(scroll.ScrollableWidth, 0, 0.5);
                Assert.InRange(scroll.ScrollableHeight, 0, 0.5);
                AssertContained(status, scroll, new Rect(0, 0, scroll.ViewportWidth, scroll.ViewportHeight));
            }
        }
        finally { window.Close(); }
        Assert.Empty(Application.Current.Windows.Cast<Window>());
    });

    [Fact]
    public Task LayoutAndMaximizePreserveVideoHostInstances() => host.RunAsync(async () =>
    {
        var api = new FakeApi();
        await using var ptz = new PtzService(api);
        var workspace = new WorkspaceViewModel(api, new FakePlayerFactory(), ptz, new InlineDispatcher(), new FakeDialogs());
        var window = LoadLayout(workspace, false);
        try
        {
            var scroll = Assert.IsType<ScrollViewer>(window.Content);
            await LayoutAsync(scroll, 1440, 900);
            var original = Descendants<VideoTileView>(scroll).ToDictionary(tile => ((VideoTileViewModel)tile.DataContext).Index);
            foreach (var count in new[] { 16, 9, 4 })
            {
                await workspace.SetLayoutCommand.ExecuteAsync(count.ToString());
                await LayoutAsync(scroll, 1440, 900);
                foreach (var tile in Descendants<VideoTileView>(scroll))
                {
                    var index = ((VideoTileViewModel)tile.DataContext).Index;
                    if (original.TryGetValue(index, out var previous)) Assert.Same(previous, tile);
                    else original.Add(index, tile);
                }
                Assert.Equal(count, Descendants<ContentPresenter>(scroll).Count(p => p.Content is VideoTileViewModel && p.Visibility == Visibility.Visible));
            }
            workspace.SelectedTile = workspace.Tiles[1];
            foreach (var maximized in new[] { true, false })
            {
                workspace.ToggleMaximizeCommand.Execute(null);
                await LayoutAsync(scroll, 1440, 900);
                Assert.Equal(maximized, workspace.IsMaximized);
                Assert.Equal(maximized ? 1 : 4, Descendants<ContentPresenter>(scroll).Count(p => p.Content is VideoTileViewModel && p.Visibility == Visibility.Visible));
                foreach (var tile in Descendants<VideoTileView>(scroll))
                    Assert.Same(original[((VideoTileViewModel)tile.DataContext).Index], tile);
            }
        }
        finally { window.Close(); }
    });

    private static Window LoadLayout(WorkspaceViewModel workspace, bool playback)
    {
        using var source = typeof(MainWindowLayoutTests).Assembly.GetManifestResourceStream("MainWindow.Layout.xaml")!;
        var document = XDocument.Load(source);
        var root = document.Root!;
        // 保留正式 XAML 的所有布局和绑定，仅移除连接生产服务的代码后置类及根窗口事件。
        root.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Remove();
        foreach (var name in new[] { "Loaded", "Closing", "Deactivated", "PreviewMouseLeftButtonUp", "PreviewKeyDown" }) root.Attribute(name)!.Remove();
        root.SetAttributeValue(XNamespace.Xmlns + "views", "clr-namespace:VideoPlatform.Desktop.Views;assembly=VideoPlatform.Desktop");
        foreach (var element in root.Descendants().Where(element => element.Name.NamespaceName == "clr-namespace:VideoPlatform.Desktop.Views"))
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:VideoPlatform.Desktop.Views;assembly=VideoPlatform.Desktop");
        var window = Assert.IsType<Window>(XamlReader.Parse(document.ToString(), new ParserContext { BaseUri = new Uri("pack://application:,,,/VideoPlatform.Desktop;component/MainWindow.xaml") }));
        window.DataContext = new
        {
            Workspace = workspace, Module = playback ? "playback" : "live", IsAuthenticated = true, CanUseWorkspace = true,
            UpdateRequired = false, UserLabel = "布局回归", VersionLabel = "布局测试", ConnectionState = "离线测试", Status = "状态栏必须可见"
        };
        Assert.False(window.IsVisible);
        return window;
    }

    private static async Task LayoutAsync(ScrollViewer root, double width, double height)
    {
        // 视口绑定和 Auto 滚动条可能需要后续布局轮次才能收敛。
        for (var i = 0; i < 4; i++)
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }

    private static void AssertContained(FrameworkElement child, Visual ancestor, Rect bounds)
    {
        var actual = child.TransformToAncestor(ancestor).TransformBounds(new Rect(child.RenderSize));
        bounds.Inflate(1, 1);
        Assert.True(bounds.Contains(actual), $"{child.GetType().Name} 超出容器：{actual}，容器：{bounds}。");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private sealed class VideoMeasureProbe : FrameworkElement
    {
        public Size Constraint { get; private set; }
        protected override Size MeasureOverride(Size availableSize)
        {
            Constraint = availableSize;
            return new Size(3840, 2160);
        }
    }
}
