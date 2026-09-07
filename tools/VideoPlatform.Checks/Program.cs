using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoPlatform.Desktop;

internal static class Program
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    [STAThread]
    private static int Main()
    {
        if (Environment.GetEnvironmentVariable("VIDEO_PLATFORM_TEST_RESTART_MARKER") is { } marker)
        {
            File.WriteAllText(marker, "更新后成功重启");
            return 0;
        }
        try
        {
            CheckDesktop();
            CheckLiveReferences();
            var api = Assembly.Load("VideoPlatform.Api").GetType("Program")!;
            var compare = api.GetMethods(Hidden).Single(method => method.Name.Contains("g__CompareReleaseVersions|"));
            Assert((int)compare.Invoke(null, ["1.0.0", "1.0.0.0"])! == 0, "等价版本触发重复升级");
            CheckUpdaterAsync().GetAwaiter().GetResult();
            Console.WriteLine("桌面分屏、树搜索、播放器释放、更新器校验检查全部通过。");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"回归检查失败：{ex}"); return 1; }
    }

    private static void CheckDesktop()
    {
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        object? Invoke(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Hidden)!.Invoke(window, args);
        var grid = (UniformGrid)window.FindName("VideoGrid");
        foreach (var count in new[] { 1, 4, 9, 16 })
        {
            Invoke("SetLayout", count);
            Assert(grid.Children.Count == count && grid.Rows * grid.Columns == count, $"{count} 分屏尺寸错误");
        }
        var views = (IList)typeof(MainWindow).GetField("_videoViews", Hidden)!.GetValue(window)!;
        var view = views[7];
        var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
        Invoke("VideoViewDoubleClick", view, mouse);
        Assert(grid.Rows == 1 && grid.Columns == 1 && grid.Children.Count == 1, "双击没有放大至完整窗口");
        Invoke("VideoViewDoubleClick", view, mouse);
        Assert(grid.Rows == 4 && grid.Children.Count == 16, "双击没有恢复原分屏");
        Invoke("SetModule", "playback");
        Assert(((FrameworkElement)window.FindName("PlaybackPanel")).Visibility == Visibility.Visible && ((FrameworkElement)window.FindName("PtzPanel")).Visibility == Visibility.Collapsed, "回放页没有切换对应工具");
        Invoke("SetModule", "settings");
        Assert(((FrameworkElement)window.FindName("MediaPanel")).Visibility == Visibility.Collapsed, "设置页没有隐藏原生视频窗口");
        Invoke("SetModule", "live");
        Invoke("SelectSlot", 7);
        Assert(((TextBlock)window.FindName("ActiveWindowText")).Text.Contains("08"), "活动窗口编号错误");
        Assert(((Border)grid.Children[7]).BorderBrush == app.FindResource("AccentBrush"), "活动窗口未高亮");
        Assert(((FrameworkElement)window.FindName("PlaybackPanel")).Visibility == Visibility.Collapsed, "主预览仍占用回放区域");

        var nodeType = typeof(MainWindow).GetNestedType("ChannelNode", Hidden)!;
        object Node(string label, bool channel = false) => Activator.CreateInstance(nodeType, label, channel, 1)!;
        bool Visible(object node) => (bool)nodeType.GetProperty("IsVisible")!.GetValue(node)!;
        var root = Node("车间");
        var children = (IList)nodeType.GetProperty("Children")!.GetValue(root)!;
        var first = Node("匹配", true);
        var last = Node("末尾通道", true);
        children.Add(first); children.Add(last);
        var notifications = 0;
        ((INotifyPropertyChanged)last).PropertyChanged += (_, _) => notifications++;
        Invoke("SetVisibility", root, "匹配");
        Assert(Visible(first) && !Visible(last) && notifications > 0, "搜索没有遍历或通知末尾节点");
        Invoke("SetVisibility", root, "车间");
        Assert(Visible(first) && Visible(last), "匹配父级时没有展示所有子节点");

        var imagePath = Path.Combine(Path.GetTempPath(), $"video-platform-check-{Guid.NewGuid():N}.png");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[] { 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0 }, 6);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(imagePath)) encoder.Save(output);
        Invoke("PlaySlot", 0, new Uri(imagePath).AbsoluteUri);
        Invoke("PlaySlot", 0, new Uri(imagePath).AbsoluteUri);
        var players = (IList)typeof(MainWindow).GetField("_players", Hidden)!.GetValue(window)!;
        Assert(players.Count == 1, "替换窗口后仍保留已释放的播放器");
        Invoke("StopPlayers");
        Invoke("StopPlayers");
        Assert(players.Count == 0 && grid.Children.Count == 16, "停止媒体破坏了分屏或未清空播放器");
        File.Delete(imagePath);
        window.Loaded -= (RoutedEventHandler)typeof(MainWindow).GetMethod("WindowLoaded", Hidden)!.CreateDelegate(typeof(RoutedEventHandler), window);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.Show();
        if (Environment.GetEnvironmentVariable("VIDEO_PLATFORM_UI_QA") is { } screenshotDirectory)
        {
            Directory.CreateDirectory(screenshotDirectory);
            var nodes = (IList)typeof(MainWindow).GetField("_channelNodes", Hidden)!.GetValue(window)!;
            var workshop = Node("生产车间");
            var workshopChildren = (IList)nodeType.GetProperty("Children")!.GetValue(workshop)!;
            workshopChildren.Add(Node("CH 01 · 东侧通道 · 在线", true));
            workshopChildren.Add(Node("CH 02 · 一层出入口 · 在线", true));
            workshopChildren.Add(Node("CH 03 · 设备区 · 离线", true));
            nodes.Add(workshop);
            foreach (var (width, height) in new[] { (1440, 900), (1000, 680) })
            {
                window.Width = width;
                window.Height = height;
                foreach (var module in new[] { "live", "playback", "alarms", "settings", "login" })
                {
                    Invoke("SetLayout", 4);
                    if (module == "login") Invoke("ShowLoginClick", window, new RoutedEventArgs());
                    else Invoke("SetModule", module);
                    window.UpdateLayout();
                    var rootVisual = (FrameworkElement)window.Content;
                    var image = new RenderTargetBitmap((int)rootVisual.ActualWidth, (int)rootVisual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    image.Render(rootVisual);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(image));
                    using var target = File.Create(Path.Combine(screenshotDirectory, $"desktop-{module}-{width}.png"));
                    png.Save(target);
                    Assert(rootVisual.ActualWidth > 0 && rootVisual.ActualHeight > 0, "窗口未正确布局");
                }
            }
            Console.WriteLine("通过：两个窗口尺寸下五个页面渲染，截图数据仅用于本地检查。");
        }
        window.Close();
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
        Assert(!window.IsVisible && (bool)typeof(MainWindow).GetField("_closeReady", Hidden)!.GetValue(window)!, "窗口关闭没有完成清理");
        Console.WriteLine("通过：1／4／9／16 分屏、双击恢复、树搜索、重复播放器释放和关闭无重入异常。");
    }

    private static async Task CheckUpdaterAsync()
    {
        var updater = Assembly.Load("VideoPlatform.Updater").GetType("VideoPlatform.Updater.Program")!;
        var succeeded = updater.GetMethod("InstallSucceeded", Hidden)!;
        foreach (var code in new[] { 0, 3010, 1641 }) Assert((bool)succeeded.Invoke(null, [code])!, $"安装成功码 {code} 被误判");
        Assert(!(bool)succeeded.Invoke(null, [1603])!, "安装失败被误判为成功");
        var options = updater.GetNestedType("Options", Hidden)!;
        var run = updater.GetMethod("RunAsync", Hidden)!;
        var bytes = Encoding.UTF8.GetBytes("仅用于下载与哈希校验的本地测试内容");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    await using var stream = client.GetStream();
                    var buffer = new byte[4096];
            await stream.ReadAsync(buffer, cancellation.Token);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), cancellation.Token);
                    await stream.WriteAsync(bytes, cancellation.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        async Task Check(string file, string hash, bool expectedSuccess, bool verifyOnly = true)
        {
            var request = Activator.CreateInstance(options, $"http://127.0.0.1:{port}/package", file, hash, Environment.ProcessPath!, int.MaxValue, Environment.ProcessPath!, false, verifyOnly)!;
            var failed = false;
            try { await (Task)run.Invoke(null, [request])!; }
            catch (Exception ex) when (!expectedSuccess && ex is InvalidDataException or InvalidOperationException) { failed = true; }
            Assert(failed != expectedSuccess, expectedSuccess ? "正确安装包执行失败" : "错误安装包通过了校验");
        }
        try
        {
            await Check("check.msi", Convert.ToHexString(SHA256.HashData(bytes)), true);
            await Check("check.msi", new string('0', 64), false);
            await Check("check.zip", Convert.ToHexString(SHA256.HashData(bytes)), false);
            if (Environment.GetEnvironmentVariable("VIDEO_PLATFORM_TEST_MSI") is { } testMsi)
            {
                await Check("invalid.msi", Convert.ToHexString(SHA256.HashData(bytes)), false, false);
                bytes = await File.ReadAllBytesAsync(testMsi);
                var marker = Path.Combine(Path.GetTempPath(), $"video-platform-restart-{Guid.NewGuid():N}.txt");
                Environment.SetEnvironmentVariable("VIDEO_PLATFORM_TEST_RESTART_MARKER", marker);
                try
                {
                    await Check("test.msi", Convert.ToHexString(SHA256.HashData(bytes)), true, false);
                    for (var attempt = 0; attempt < 30 && !File.Exists(marker); attempt++) await Task.Delay(200);
                    Assert(File.Exists(marker), "安装成功后没有启动目标程序");
                    File.Delete(marker);
                    Console.WriteLine("通过：测试 MSI 实际安装、损坏 MSI 失败和安装后重启。");
                    if (Environment.GetEnvironmentVariable("VIDEO_PLATFORM_TEST_FAIL_MSI") is { } failMsi)
                    {
                        bytes = await File.ReadAllBytesAsync(failMsi);
                        await Check("failed-upgrade.msi", Convert.ToHexString(SHA256.HashData(bytes)), false, false);
                        Assert(File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPlatform-Updater-QA", "updater-test-marker.txt")), "失败升级没有恢复旧版本文件");
                        Console.WriteLine("通过：失败升级触发 MSI 事务回滚，旧版本文件保留。");
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("VIDEO_PLATFORM_TEST_RESTART_MARKER", null);
                    var uninstall = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe")) { UseShellExecute = false };
                    foreach (var argument in new[] { "/x", testMsi, "/qn", "/norestart" }) uninstall.ArgumentList.Add(argument);
                    using var process = System.Diagnostics.Process.Start(uninstall)!;
                    await process.WaitForExitAsync();
                    Assert(process.ExitCode is 0 or 1605 or 3010, $"测试 MSI 卸载失败：{process.ExitCode}");
                }
            }
            Console.WriteLine("通过：正确哈希、错误哈希、非 MSI 拒绝，以及 Windows Installer 成功／失败退出码。");
        }
        finally { cancellation.Cancel(); listener.Stop(); await server; }
    }

    private static void CheckLiveReferences()
    {
        var type = Assembly.Load("Hikvision.Adapter").GetType("HikvisionProbe")!;
        var probe = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var references = new Dictionary<(int Channel, int StreamType), int> { [(8, 2)] = 2 };
        var sessions = new Dictionary<Guid, (int Channel, int StreamType)>();
        type.GetField("_liveReferences", Hidden)!.SetValue(probe, references);
        type.GetField("_liveSessionKeys", Hidden)!.SetValue(probe, sessions);
        type.GetField("_liveGate", Hidden)!.SetValue(probe, new object());
        var id = Guid.NewGuid();
        type.GetMethod("StartLive")!.Invoke(probe, [8, 2, id]);
        type.GetMethod("StartLive")!.Invoke(probe, [8, 2, id]);
        Assert(references[(8, 2)] == 3, "重复建立同一会话增加了引用计数");
        type.GetMethod("StopLive")!.Invoke(probe, [8, 2, id]);
        type.GetMethod("StopLive")!.Invoke(probe, [8, 2, id]);
        Assert(references[(8, 2)] == 2, "重复停止会话释放了其他用户的引用");
        Console.WriteLine("通过：实时会话重复建立／停止不影响其他用户引用。");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
