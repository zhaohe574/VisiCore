using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using VideoPlatform.Desktop.Views;

namespace VideoPlatform.Desktop.Services;

/// <summary>
/// 全局底层鼠标钩子（WH_MOUSE_LL）：
/// 彻底解决 LibVLC Direct3D 原生子窗口（运行于独立的 vout 线程）在视频区域拦截并吞噬鼠标右键/左键点击的问题。
/// 零空域冲突：在 Windows 消息分发给 Direct3D 窗口之前，由本钩子捕获并直接触发对应窗格的选中、双击放大与右键上下文菜单。
/// </summary>
public static class VideoMouseHook
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    public static event Action<Point>? MouseMoved;
    public static Func<Point, bool>? IsOverFullscreenToolbar;
    private static Point _lastReportedPt;
    private static long _lastReportedTick;
    private static WeakReference<VideoTileView>? _currentHoveredTileRef;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private static LowLevelMouseProc? _hookProc;
    private static IntPtr _hookId = IntPtr.Zero;
    private static readonly List<WeakReference<VideoTileView>> _activeTiles = [];
    private static readonly object _sync = new();
    private static DateTime _lastLeftDownTime = DateTime.MinValue;
    private static System.Drawing.Point _lastLeftDownPt;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT Point);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static void RegisterTile(VideoTileView tile)
    {
        lock (_sync)
        {
            _activeTiles.RemoveAll(wr => !wr.TryGetTarget(out var t) || ReferenceEquals(t, tile));
            _activeTiles.Add(new WeakReference<VideoTileView>(tile));
        }
    }

    public static void UnregisterTile(VideoTileView tile)
    {
        lock (_sync)
        {
            _activeTiles.RemoveAll(wr => !wr.TryGetTarget(out var t) || ReferenceEquals(t, tile));
        }
        if (_currentHoveredTileRef?.TryGetTarget(out var current) == true && ReferenceEquals(current, tile))
        {
            _currentHoveredTileRef = null;
        }
    }

    public static void Install()
    {
        if (_hookId != IntPtr.Zero) return;
        _hookProc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(curModule?.ModuleName), 0);
    }

    public static void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _hookProc = null;
        }
        _currentHoveredTileRef = null;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg == WM_MOUSEMOVE)
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var nowTick = Environment.TickCount64;
                    if (nowTick - _lastReportedTick >= 25 || Math.Abs(hookStruct.pt.x - _lastReportedPt.X) >= 4 || Math.Abs(hookStruct.pt.y - _lastReportedPt.Y) >= 4)
                    {
                        _lastReportedTick = nowTick;
                        _lastReportedPt = new Point(hookStruct.pt.x, hookStruct.pt.y);
                        OnGlobalMouseMove(_lastReportedPt);
                        MouseMoved?.Invoke(_lastReportedPt);
                    }
                }
                catch { }
            }
            else if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_RBUTTONUP)
            {
                try
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (HandleMouseMessage(msg, hookStruct))
                    {
                        return (IntPtr)1; // 消费消息，彻底阻止底层 VLC 窗口抢占焦点与吞噬右键
                    }
                }
                catch
                {
                    // 尽力处理，异常时放行
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static void OnGlobalMouseMove(Point screenPt)
    {
        // 1. 检查鼠标所在窗口是否属于本进程
        var hwnd = WindowFromPoint(new POINT { x = (int)screenPt.X, y = (int)screenPt.Y });
        if (hwnd == IntPtr.Zero)
        {
            ClearHoveredTile();
            return;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != (uint)Environment.ProcessId)
        {
            // 鼠标不在本程序窗口上（如在桌面或其它应用程序上方），立即隐藏任何分屏浮动菜单
            ClearHoveredTile();
            return;
        }

        // 2. 检查主窗口状态：最小化或未显示时不响应
        var mainWin = Application.Current?.MainWindow;
        if (mainWin is null || !mainWin.IsVisible || mainWin.WindowState == WindowState.Minimized)
        {
            ClearHoveredTile();
            return;
        }

        // 3. 检查鼠标是否在主窗口可视客户区内
        try
        {
            var winPt = mainWin.PointFromScreen(screenPt);
            if (winPt.X < 0 || winPt.X >= mainWin.ActualWidth || winPt.Y < 0 || winPt.Y >= mainWin.ActualHeight)
            {
                ClearHoveredTile();
                return;
            }
        }
        catch
        {
            ClearHoveredTile();
            return;
        }

        // 4. 查找鼠标落在哪一个【当前活跃】且【正在播放】的分屏窗格内
        VideoTileView? hoveredTile = null;

        lock (_sync)
        {
            for (var i = _activeTiles.Count - 1; i >= 0; i--)
            {
                if (!_activeTiles[i].TryGetTarget(out var tile))
                {
                    _activeTiles.RemoveAt(i);
                    continue;
                }

                // 核心过滤：窗格必须处于可见状态、在当前分屏档位排布中活跃 (IsActive)、且正在播放
                if (!tile.IsVisible || tile.ActualWidth <= 0 || tile.ActualHeight <= 0) continue;
                if (tile.Tile is not { IsActive: true, IsPlaying: true }) continue;

                Point localPt;
                try
                {
                    localPt = tile.PointFromScreen(screenPt);
                }
                catch
                {
                    continue;
                }

                if (localPt.X >= 0 && localPt.X < tile.ActualWidth && localPt.Y >= 0 && localPt.Y < tile.ActualHeight)
                {
                    hoveredTile = tile;
                    break;
                }
            }
        }

        VideoTileView? prevTile = null;
        _currentHoveredTileRef?.TryGetTarget(out prevTile);

        if (!ReferenceEquals(hoveredTile, prevTile))
        {
            lock (_sync)
            {
                for (var i = _activeTiles.Count - 1; i >= 0; i--)
                {
                    if (_activeTiles[i].TryGetTarget(out var t))
                    {
                        if (!ReferenceEquals(t, hoveredTile) && t.IsTileHeaderVisible)
                        {
                            _ = t.Dispatcher.InvokeAsync(t.HideHeader);
                        }
                    }
                    else
                    {
                        _activeTiles.RemoveAt(i);
                    }
                }
            }
            if (hoveredTile is not null)
            {
                _ = hoveredTile.Dispatcher.InvokeAsync(hoveredTile.ShowHeader);
                _currentHoveredTileRef = new WeakReference<VideoTileView>(hoveredTile);
            }
            else
            {
                _currentHoveredTileRef = null;
            }
        }
        else if (hoveredTile is not null)
        {
            _ = hoveredTile.Dispatcher.InvokeAsync(hoveredTile.ShowHeader);
        }
    }

    public static void ClearHoveredTile()
    {
        lock (_sync)
        {
            for (var i = _activeTiles.Count - 1; i >= 0; i--)
            {
                if (_activeTiles[i].TryGetTarget(out var tile))
                {
                    if (tile.IsTileHeaderVisible)
                    {
                        _ = tile.Dispatcher.InvokeAsync(tile.HideHeader);
                    }
                }
                else
                {
                    _activeTiles.RemoveAt(i);
                }
            }
        }
        _currentHoveredTileRef = null;
    }

    private static bool HandleMouseMessage(int msg, MSLLHOOKSTRUCT hookStruct)
    {
        // 若当前属于其它进程窗口，不予拦截
        var hwnd = WindowFromPoint(hookStruct.pt);
        if (hwnd == IntPtr.Zero) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != Environment.ProcessId) return false;

        // 若当前有上下文菜单处于打开状态，放行以便用户点击菜单项或点击外部关闭菜单
        if (IsAnyContextMenuOpen()) return false;

        var screenPt = new Point(hookStruct.pt.x, hookStruct.pt.y);

        // 若鼠标落在全屏悬浮工具栏或退出按钮上，放行以便点击底栏按钮与退出全屏
        if (IsOverFullscreenToolbar?.Invoke(screenPt) == true) return false;

        VideoTileView? targetTile = null;

        lock (_sync)
        {
            for (var i = _activeTiles.Count - 1; i >= 0; i--)
            {
                if (!_activeTiles[i].TryGetTarget(out var tile))
                {
                    _activeTiles.RemoveAt(i);
                    continue;
                }

                if (!tile.IsVisible || tile.ActualWidth <= 0 || tile.ActualHeight <= 0) continue;
                if (tile.Tile?.IsActive != true) continue;

                Point localPt;
                try
                {
                    localPt = tile.PointFromScreen(screenPt);
                }
                catch
                {
                    continue;
                }

                if (localPt.X >= 0 && localPt.X < tile.ActualWidth && localPt.Y >= 0 && localPt.Y < tile.ActualHeight)
                {
                    // 判断是否落在顶部操作工具条区域。
                    // 若在工具条上且工具条处于可见状态：
                    // 如果是左键点击，放行给浮动按钮（声音、抓图、放大、停止）正常响应
                    // 如果是右键点击，仍然允许弹出该窗格的右键菜单
                    if (tile.IsTileHeaderVisible && localPt.Y < (tile.TileHeaderBar?.ActualHeight ?? 26))
                    {
                        if (msg == WM_LBUTTONDOWN)
                        {
                            return false;
                        }
                    }
                    targetTile = tile;
                    break;
                }
            }
        }

        if (targetTile is null) return false;

        switch (msg)
        {
            case WM_LBUTTONDOWN:
                var now = DateTime.UtcNow;
                var dblTime = GetDoubleClickTime();
                var isDoubleClick = (now - _lastLeftDownTime).TotalMilliseconds <= dblTime
                    && Math.Abs(hookStruct.pt.x - _lastLeftDownPt.X) < 4
                    && Math.Abs(hookStruct.pt.y - _lastLeftDownPt.Y) < 4;
                _lastLeftDownTime = now;
                _lastLeftDownPt = new System.Drawing.Point(hookStruct.pt.x, hookStruct.pt.y);

                targetTile.Dispatcher.InvokeAsync(() =>
                {
                    targetTile.SelectTile();
                    if (isDoubleClick)
                    {
                        targetTile.ToggleMaximize();
                    }
                });
                return true;

            case WM_RBUTTONDOWN:
                targetTile.Dispatcher.InvokeAsync(targetTile.SelectTile);
                return true;

            case WM_RBUTTONUP:
                targetTile.Dispatcher.InvokeAsync(() =>
                {
                    targetTile.SelectTile();
                    targetTile.OpenTileContextMenuAt(screenPt);
                });
                return true;
        }

        return false;
    }

    private static bool IsAnyContextMenuOpen()
    {
        lock (_sync)
        {
            foreach (var wr in _activeTiles)
            {
                if (wr.TryGetTarget(out var tile) && tile.ContextMenu?.IsOpen == true)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
