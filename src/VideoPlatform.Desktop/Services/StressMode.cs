using System.Globalization;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Services;

/// <summary>
/// 值守压力测试模式。仅当设置 <c>VISICORE_STRESS=1</c> 时生效：
/// 自动登录、加载指定分屏档位并在指定秒数后自动退出，用于以可复现的方式采集 CPU／显卡占用。
/// 生产环境未设置该变量时本类型不产生任何行为，也不读取任何凭据。
///
/// 为避免采集过程影响值守机与平台，本模式具备三重保护：
/// 1) 看门狗：到时无条件调用 Shutdown，即使加载阶段卡住也会退出；
/// 2) 会话归还：退出前逐条停止本次创建的媒体会话，避免平台侧会话堆积；
/// 3) 启动前清理：加载前先清理本账号的历史孤儿会话。
/// </summary>
internal static class StressMode
{
    /// <summary>压力测试使用的媒体传输覆盖值：ts／flv／rtsp，空表示按客户端设置决定。</summary>
    internal static string? StressMediaOverride { get; private set; }

    public static void Attach(ShellViewModel shell)
    {
        if (Environment.GetEnvironmentVariable("VISICORE_STRESS") != "1") return;
        _ = RunAsync(shell);
    }

    private static async Task RunAsync(ShellViewModel shell)
    {
        // 看门狗先于一切启动：只要进了压力测试模式，就一定会自己退出。
        var watchdog = StartWatchdog();
        var openedSessions = new List<string>();
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("VISICORE_STRESS_SECONDS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        try
        {
            var user = Require("VISICORE_STRESS_USER");
            var password = Require("VISICORE_STRESS_PASSWORD");
            var layout = Environment.GetEnvironmentVariable("VISICORE_STRESS_LAYOUT") ?? "4";
            // 加载几路（默认等于档位格数）；设为 0 可只做“启动 + 登录 + 资源列表”的启动诊断。
            var loadCount = int.TryParse(Environment.GetEnvironmentVariable("VISICORE_STRESS_LOAD"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var load) ? load : -1;
            var shrink = double.TryParse(Environment.GetEnvironmentVariable("VISICORE_STRESS_SHRINK"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ? scale : 1.0;
            var transport = Environment.GetEnvironmentVariable("VISICORE_STRESS_TRANSPORT");
            if (!string.IsNullOrWhiteSpace(transport)) StressMediaOverride = transport.Trim().ToLowerInvariant();
            if (shrink is > 0 and < 1 && System.Windows.Application.Current?.MainWindow is { } window)
            {
                window.Width = Math.Max(640, window.Width * shrink);
                window.Height = Math.Max(420, window.Height * shrink);
                ClientFiles.Log($"压力测试模式窗口缩放比例 {shrink}：{window.Width:0}×{window.Height:0}。");
            }

            Milestone("压力测试模式启动前");
            ClientFiles.Log($"压力测试模式启动：分屏 {layout}，加载 {loadCount} 路，运行 {seconds} 秒，传输 {transport ?? "默认"}。");
            // 压力测试不受“必须更新后才能登录”的限制：目标平台可能仍在运行被撤回的旧版本，
            // 而性能采集需要在同一平台上对比优化前后的占用。
            await shell.LoginAsync(user, password, enforceUpdateGate: false);
            Milestone("登录完成");
            if (!shell.IsAuthenticated)
            {
                ClientFiles.Log("压力测试模式登录未成功，终止。");
                return;
            }
            shell.Theme = "light";
            Milestone("资源列表就绪");
            // 上一次采集可能被强制中断而留下孤儿会话，先归还再开始新一轮。
            try { await shell.Workspace.PurgeOrphanSessionsAsync(); } catch (Exception ex) { ClientFiles.Log($"压力测试模式清理历史会话失败：{ex.Message}"); }

            await shell.Workspace.SetLayoutCommand.ExecuteAsync(layout);
            Milestone($"分屏切换到 {layout}");
            var wanted = loadCount < 0 ? shell.Workspace.LayoutCount : Math.Min(loadCount, shell.Workspace.LayoutCount);
            var channels = shell.Workspace.Resources
                .SelectMany(node => node.Flatten())
                .Where(node => node.Channel is { Online: true })
                .Select(node => node.Channel!)
                .Take(wanted)
                .ToArray();
            var opened = 0;
            foreach (var channel in channels)
            {
                var tile = shell.Workspace.VisibleTiles.ElementAtOrDefault(opened);
                if (tile is null) break;
                try
                {
                    await shell.Workspace.OpenChannelAsync(channel, tile);
                    if (tile.SessionId is { } sessionId) openedSessions.Add(sessionId);
                    opened++;
                    if (opened == 1 || opened % 4 == 0) Milestone($"已加载 {opened} 路");
                }
                catch (Exception ex)
                {
                    ClientFiles.Log($"压力测试模式打开通道 {channel.Name} 失败：{ex.Message}");
                }
            }
            shell.Workspace.SelectedTile = shell.Workspace.Tiles[0];
            shell.RefreshDiagnostics();
            Milestone($"加载 {opened} 路完成");
            ClientFiles.Log($"压力测试模式已加载 {opened} 路视频，分屏 {shell.Workspace.LayoutCount}，会话 {shell.SessionCount}，解码器 {shell.DecoderCount}。");

            var targetModule = Environment.GetEnvironmentVariable("VISICORE_STRESS_MODULE");
            if (!string.IsNullOrWhiteSpace(targetModule))
            {
                await shell.SwitchModuleCommand.ExecuteAsync(targetModule);
                Milestone($"切换模块到 {targetModule}");
            }
            var targetTab = Environment.GetEnvironmentVariable("VISICORE_STRESS_SETTINGS_TAB");
            if (!string.IsNullOrWhiteSpace(targetTab))
            {
                shell.SetSettingsTabCommand.Execute(targetTab);
                Milestone($"切换设置子标签到 {targetTab}");
            }
            var targetDialog = Environment.GetEnvironmentVariable("VISICORE_STRESS_DIALOG")?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(targetDialog))
            {
                ClientFiles.Log($"压力测试模式配置了弹窗请求：{targetDialog}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000);
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher is not null)
                        {
                            _ = dispatcher.InvokeAsync(() =>
                            {
                                ClientFiles.Log($"准备打开弹窗：{targetDialog}");
                                if (targetDialog == "profile") shell.OpenProfileCommand.Execute(null);
                                else if (targetDialog == "password") shell.OpenChangePasswordCommand.Execute(null);
                                ClientFiles.Log($"弹窗命令执行完毕：{targetDialog}");
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        ClientFiles.Log($"打开弹窗异常：{ex}");
                    }
                });
            }

            if (opened > 0)
            {
                // 解码证明：请求的传输方式必须真的在推进播放，否则“低占用”可能只是静默失败。
                var firstPositions = await SamplePositionsAsync(shell);
                await Task.Delay(TimeSpan.FromSeconds(20));
                var secondPositions = await SamplePositionsAsync(shell);
                var advanced = 0;
                var stalled = 0;
                foreach (var (tile, position) in secondPositions)
                {
                    if (firstPositions.TryGetValue(tile, out var previous) && position > previous) advanced++;
                    else stalled++;
                }
                ClientFiles.Log($"压力测试解码证明：{advanced}/{secondPositions.Count} 路播放位置在 20 秒内推进，停滞后进 {stalled} 路，诊断标签「{shell.DiagnosticsLabel}」。");
            }
            Milestone("采样点就绪");

            if (seconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                ClientFiles.Log("压力测试模式到时退出。");
            }
            else
            {
                // 0 表示持续运行不自动退出，供用户人工直观检验 16 路画面与交互响应
                watchdog.Cancel();
                ClientFiles.Log("压力测试模式已进入持续运行模式（0 秒不自动退出）。");
                return;
            }
        }
        catch (Exception ex)
        {
            ClientFiles.Log($"压力测试模式异常：{ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            if (seconds > 0)
            {
                watchdog.Cancel();
                // 归还本次创建的媒体会话，避免平台侧堆积影响后续值守与采集。
                await ReleaseSessionsAsync(shell, openedSessions);
                ClientFiles.Log($"压力测试模式结束：已归还 {openedSessions.Count} 条会话。");
                Milestone("退出前");
                Exit(0);
            }
        }
    }

    private static async Task ReleaseSessionsAsync(ShellViewModel shell, List<string> sessions)
    {
        // 逐格停止会归还各自的服务端租约；随后再按账号清理一次，覆盖加载失败留下的半开会话。
        try { await shell.Workspace.StopAllCoreAsync(); }
        catch (Exception ex) { ClientFiles.Log($"压力测试模式停止窗口失败：{ex.Message}"); }
        try { await shell.Workspace.PurgeOrphanSessionsAsync(); }
        catch (Exception ex) { ClientFiles.Log($"压力测试模式清理会话失败：{ex.Message}"); }
    }

    private static CancellationTokenSource StartWatchdog()
    {
        var cts = new CancellationTokenSource();
        var limit = int.TryParse(Environment.GetEnvironmentVariable("VISICORE_STRESS_WATCHDOG_SECONDS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 420;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(limit), cts.Token);
                ClientFiles.Log($"压力测试模式看门狗超时（{limit} 秒），强制退出。");
                if (System.Windows.Application.Current is { } application)
                    application.Dispatcher.Invoke(() => application.Shutdown(9));
            }
            catch (OperationCanceledException) { }
        });
        return cts;
    }

    /// <summary>记录关键里程碑的进程与系统内存，用于定位“启动即卡死”发生在哪一步。</summary>
    private static void Milestone(string stage)
    {
        try
        {
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            self.Refresh();
            var managedMb = GC.GetTotalMemory(false) / 1024 / 1024;
            var gcInfo = GC.GetGCMemoryInfo();
            var totalMb = gcInfo.TotalAvailableMemoryBytes / 1024 / 1024;
            // 容器/进程可用内存上限减去当前托管堆占用，作为“还剩多少可分配”的粗略指标。
            var remainingMb = totalMb - self.WorkingSet64 / 1024 / 1024;
            ClientFiles.Log($"启动里程碑[{stage}]：进程 {self.WorkingSet64 / 1024 / 1024} MB（托管 {managedMb} MB），线程 {self.Threads.Count}，句柄 {self.HandleCount}，可用上限 {totalMb} MB，余量约 {remainingMb} MB。");
        }
        catch (Exception ex) { ClientFiles.Log($"启动里程碑[{stage}]采集失败：{ex.Message}"); }
    }

    private static void Exit(int code)
    {
        if (System.Windows.Application.Current is { } application) application.Shutdown(code);
    }

    private static Task<Dictionary<string, long>> SamplePositionsAsync(ShellViewModel shell) =>
        System.Windows.Application.Current!.Dispatcher.InvokeAsync(() =>
        {
            var map = new Dictionary<string, long>();
            foreach (var tile in shell.Workspace.VisibleTiles)
            {
                var position = tile.PlaybackPositionMs;
                map[$"窗口{tile.Number}"] = position ?? -1;
            }
            return map;
        }).Task;

    private static string Require(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"压力测试模式缺少环境变量 {name}。");
}
