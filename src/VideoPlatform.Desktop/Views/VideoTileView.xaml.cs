using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class VideoTileView : UserControl
{
    private static readonly FieldInfo? VideoHwndHostField = typeof(LibVLCSharp.WPF.VideoView).GetField(
        "_videoHwndHost", BindingFlags.NonPublic | BindingFlags.Instance);

    private VideoTileViewModel? _tile;
    private LibVLCSharp.WPF.VideoView? _video;
    private string? _lastAppliedRatio;
    private bool? _lastVideoVisible;
    private readonly SubclassWndProc _subclassProc;

    private System.Windows.Threading.DispatcherTimer? _headerIdleTimer;

    public VideoTileView()
    {
        _subclassProc = VideoSubclassHandler;
        InitializeComponent();
        TileHeaderPopup.PlacementTarget = this;
        TileHeaderPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Relative;
        TileHeaderPopup.HorizontalOffset = 0;
        TileHeaderPopup.VerticalOffset = 0;

        _headerIdleTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _headerIdleTimer.Tick += (_, _) =>
        {
            _headerIdleTimer.Stop();
            HideHeader();
        };

        DataContextChanged += (_, _) =>
        {
            if (_tile is not null) _tile.PropertyChanged -= TileChanged;
            _tile = DataContext as VideoTileViewModel;
            if (_tile is not null) _tile.PropertyChanged += TileChanged;
            TileHeaderPopup.PlacementTarget = this;
            TileHeaderPopup.DataContext = _tile;
            TileHeaderBar.DataContext = _tile;
            _lastAppliedRatio = null;
            Tag ??= Workspace;
            UpdatePlayer();
            UpdateTileAspectRatio();
            if (_tile is not { IsPlaying: true, IsActive: true }) HideHeader();
        };
        IsVisibleChanged += (_, _) =>
        {
            // 离屏（单窗放大/多分屏还原）时通过 Visibility = Collapsed 隐藏原生窗口，切勿销毁 ReleaseView()，
            // 否则放大或还原时其余路数全部丢失 HWND 造成黑屏重载。
            if (IsVisible) UpdatePlayer();
            SyncVisibility();
            if (IsVisible) UpdateTileAspectRatio();
            else HideHeader();
        };
        Loaded += (_, _) =>
        {
            Tag ??= Workspace;
            VideoPlatform.Desktop.Services.VideoMouseHook.RegisterTile(this);
            UpdatePlayer();
            SyncVisibility();
            UpdateTileAspectRatio();
        };
        SizeChanged += (_, _) =>
        {
            if (TileHeaderPopup.IsOpen)
            {
                TileHeaderPopup.Width = Math.Max(0, ActualWidth);
                TileHeaderBar.Width = Math.Max(0, ActualWidth);
                var offset = TileHeaderPopup.HorizontalOffset;
                TileHeaderPopup.HorizontalOffset = offset + 0.0001;
                TileHeaderPopup.HorizontalOffset = offset;
            }
            UpdateTileAspectRatio();
        };
        Unloaded += (_, _) =>
        {
            HideHeader();
            VideoPlatform.Desktop.Services.VideoMouseHook.UnregisterTile(this);
            SyncVisibility();
            ReleaseView();
        };
    }

    public VideoTileViewModel? Tile => _tile;

    public void ToggleMaximize()
    {
        SelectTile();
        Workspace?.ToggleMaximizeCommand.Execute(null);
    }

    private void TileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoTileViewModel.NativePlayer))
        {
            UpdatePlayer();
            UpdateTileAspectRatio();
        }
        else if (e.PropertyName == nameof(VideoTileViewModel.IsActive))
        {
            if (_tile?.IsActive != true) HideHeader();
        }
        else if (e.PropertyName == nameof(VideoTileViewModel.IsPlaying))
        {
            if (_tile?.IsPlaying == true)
            {
                EnsurePlayerHwnd();
                AttachSubclass();
                _lastAppliedRatio = null;
                UpdateTileAspectRatio();
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    for (var i = 1; i <= 4; i++)
                    {
                        await Task.Delay(150 * i);
                        if (_tile?.IsPlaying != true) break;
                        EnsurePlayerHwnd();
                        AttachSubclass();
                        UpdateTileAspectRatio();
                    }
                });
            }
            else
            {
                HideHeader();
            }
        }
        else if (e.PropertyName == nameof(VideoTileViewModel.AspectRatio))
        {
            _lastAppliedRatio = null;
            UpdateTileAspectRatio();
        }
        else if (e.PropertyName == nameof(VideoTileViewModel.IsVisible))
        {
            SyncVisibility();
        }
    }

    public void SyncOverlayVisibility() => SyncVisibility();

    private void SyncVisibility()
    {
        var visible = IsVisible && (_tile?.IsVisible ?? false);
        if (_video is null || _lastVideoVisible == visible) return;
        _lastVideoVisible = visible;
        _video.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePlayer()
    {
        if (_tile?.NativePlayer is null)
        {
            if (_video is not null)
            {
                DetachSubclass();
                if (_video.MediaPlayer is { } p) p.Vout -= OnPlayerVout;
                _video.MediaPlayer = null;
                _video.Visibility = Visibility.Collapsed;
                _lastVideoVisible = false;
            }
            return;
        }
        // 分屏放大和模块切换只隐藏宿主，避免播放期间重建原生窗口后失去视频输出。
        if (!IsVisible) { SyncVisibility(); return; }
        if (_video is null)
        {
            // 关键：VideoView.Content 严格保持为 null，杜绝 LibVLCSharp 生成 16 个透明 ForegroundWindow 压垮 DWM
            _video = new LibVLCSharp.WPF.VideoView
            {
                Content = null,
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _video.ApplyTemplate();
            _video.Loaded += (_, _) =>
            {
                EnsurePlayerHwnd();
                AttachSubclass();
            };
            PlayerHost.Content = _video;
            _lastVideoVisible = null;
        }
        EnsurePlayerHwnd();
        if (!ReferenceEquals(_video.MediaPlayer, _tile.NativePlayer))
        {
            if (_video.MediaPlayer is { } oldPlayer) oldPlayer.Vout -= OnPlayerVout;
            _video.MediaPlayer = _tile.NativePlayer;
            if (_tile.NativePlayer is { } newPlayer) newPlayer.Vout += OnPlayerVout;
        }
        EnsurePlayerHwnd();
        AttachSubclass();
        SyncVisibility();
    }

    private void OnPlayerVout(object? sender, EventArgs e)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            EnsurePlayerHwnd();
            AttachSubclass();
            _lastAppliedRatio = null;
            UpdateTileAspectRatio();
            await Task.Delay(100);
            EnsurePlayerHwnd();
            AttachSubclass();
            UpdateTileAspectRatio();
        });
    }

    private void EnsurePlayerHwnd()
    {
        try
        {
            var hostHandle = GetVideoHwnd();
            if (hostHandle != IntPtr.Zero && _tile?.NativePlayer is { } player)
            {
                if (player.Hwnd != hostHandle)
                {
                    player.Hwnd = hostHandle;
                }
            }
        }
        catch { }
    }

    private void ReleaseView()
    {
        _lastAppliedRatio = null;
        _lastVideoVisible = null;
        if (_video is null) return;
        DetachSubclass();
        if (_video.MediaPlayer is { } player)
        {
            player.Vout -= OnPlayerVout;
        }
        _video.MediaPlayer = null;
        PlayerHost.Content = null;
        _video.Dispose();
        _video = null;
    }

    #region Win32 Subclassing (零透明窗口捕获视频区域鼠标交互)

    private const uint WM_PARENTNOTIFY = 0x0210;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private IntPtr VideoSubclassHandler(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        switch (uMsg)
        {
            case WM_PARENTNOTIFY:
                if ((wParam.ToInt64() & 0xFFFF) == 1) // WM_CREATE
                {
                    var childHwnd = lParam;
                    if (childHwnd != IntPtr.Zero)
                    {
                        SetWindowSubclass(childHwnd, _subclassProc, (UIntPtr)1, UIntPtr.Zero);
                        _ = Dispatcher.InvokeAsync(() => AttachSubclass());
                    }
                }
                break;
            case WM_LBUTTONDOWN:
                _ = Dispatcher.InvokeAsync(SelectTile);
                break;
            case WM_LBUTTONDBLCLK:
                _ = Dispatcher.InvokeAsync(() =>
                {
                    SelectTile();
                    Workspace?.ToggleMaximizeCommand.Execute(null);
                });
                break;
            case WM_RBUTTONDOWN:
                _ = Dispatcher.InvokeAsync(SelectTile);
                return IntPtr.Zero;
            case WM_CONTEXTMENU:
            case WM_RBUTTONUP:
                _ = Dispatcher.InvokeAsync(() =>
                {
                    SelectTile();
                    OpenTileContextMenu();
                });
                return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void SelectTile()
    {
        if (DataContext is VideoTileViewModel tile && Workspace is { } workspace)
            workspace.SelectedTile = tile;
    }

    public void OpenTileContextMenu() => OpenTileContextMenuAt(null);

    public void OpenTileContextMenuAt(Point? screenPt)
    {
        Tag ??= Workspace;
        if (ContextMenu is { } menu)
        {
            menu.PlacementTarget = this;
            if (Workspace is { } ws)
            {
                menu.DataContext = ws;
            }
            if (screenPt.HasValue)
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
                menu.HorizontalOffset = screenPt.Value.X;
                menu.VerticalOffset = screenPt.Value.Y;
            }
            else
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            }
            menu.IsOpen = false;
            menu.IsOpen = true;
        }
    }

    private void AttachSubclass()
    {
        try
        {
            var hostHandle = GetVideoHwnd();
            if (hostHandle == IntPtr.Zero) return;
            SetWindowSubclass(hostHandle, _subclassProc, (UIntPtr)1, UIntPtr.Zero);
            EnumChildWindows(hostHandle, (child, _) =>
            {
                SetWindowSubclass(child, _subclassProc, (UIntPtr)1, UIntPtr.Zero);
                EnableWindow(child, false);
                return true;
            }, IntPtr.Zero);
            MainWindow.EnsureChildWindowsBlack(hostHandle);
        }
        catch { }
    }

    private void DetachSubclass()
    {
        try
        {
            var hostHandle = GetVideoHwnd();
            if (hostHandle == IntPtr.Zero) return;
            RemoveWindowSubclass(hostHandle, _subclassProc, (UIntPtr)1);
            EnumChildWindows(hostHandle, (child, _) =>
            {
                RemoveWindowSubclass(child, _subclassProc, (UIntPtr)1);
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    private IntPtr GetVideoHwnd()
    {
        if (_video is null) return IntPtr.Zero;
        if (VideoHwndHostField?.GetValue(_video) is System.Windows.Interop.HwndHost host)
            return host.Handle;
        return IntPtr.Zero;
    }

    private delegate IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassWndProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassWndProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    #endregion

    private WorkspaceViewModel? Workspace => (Tag as WorkspaceViewModel)
        ?? (WorkspaceViewInstance?.DataContext as WorkspaceViewModel)
        ?? (Application.Current?.MainWindow?.DataContext as ShellViewModel)?.Workspace;

    private WorkspaceView? WorkspaceViewInstance
    {
        get
        {
            DependencyObject? parent = this;
            while (parent is not null)
            {
                if (parent is WorkspaceView view) return view;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        DependencyObject? node = current;
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void TileMouseLeftDown(object sender, MouseButtonEventArgs e) => HandleLeftDown(e);

    private void TileMouseRightDown(object sender, MouseButtonEventArgs e) => HandleRightDown(e);

    private void HandleLeftDown(MouseButtonEventArgs e)
    {
        if (DataContext is not VideoTileViewModel tile || Workspace is not { } workspace) return;
        workspace.SelectedTile = tile;
        if (e.ClickCount == 2)
        {
            workspace.ToggleMaximizeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // 若点击的是顶部工具条中的按钮（声音、抓图、放大、停止），绝不能吞噬事件，放行给 Button 触发点击与命令
        if (e.OriginalSource is DependencyObject dep && FindVisualAncestor<System.Windows.Controls.Primitives.ButtonBase>(dep) is not null)
        {
            return;
        }
    }

    private void HandleRightDown(MouseButtonEventArgs e)
    {
        if (DataContext is not VideoTileViewModel tile || Workspace is not { } workspace) return;
        workspace.SelectedTile = tile;
    }

    private async void Dropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Channel)) is Channel channel && DataContext is VideoTileViewModel tile && Workspace is { } workspace)
            await workspace.OpenChannelAsync(channel, tile);
        e.Handled = true;
    }

    private async void SwitchToMainStreamClicked(object sender, RoutedEventArgs e)
    {
        var tile = _tile ?? DataContext as VideoTileViewModel;
        if (tile is not null && Workspace is not null)
        {
            try
            {
                tile.ManualStreamOverride = 1;
                await tile.SwitchStreamAsync(1);
                Workspace.Status = $"窗口 {tile.Number} 已切为主码流。";
            }
            catch (Exception ex)
            {
                Workspace.Status = $"窗口 {tile.Number} 切换主码流失败：{ex.Message}";
            }
        }
    }

    private async void SwitchToSubStreamClicked(object sender, RoutedEventArgs e)
    {
        var tile = _tile ?? DataContext as VideoTileViewModel;
        if (tile is not null && Workspace is not null)
        {
            try
            {
                tile.ManualStreamOverride = 2;
                await tile.SwitchStreamAsync(2);
                Workspace.Status = $"窗口 {tile.Number} 已切为子码流。";
            }
            catch (Exception ex)
            {
                Workspace.Status = $"窗口 {tile.Number} 切换子码流失败：{ex.Message}";
            }
        }
    }

    public bool IsTileHeaderVisible => TileHeaderPopup.IsOpen;

    public void ShowHeader()
    {
        if (_tile is not { IsPlaying: true, IsActive: true } || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
        {
            HideHeader();
            return;
        }
        TileHeaderPopup.PlacementTarget = this;
        TileHeaderPopup.DataContext = _tile;
        TileHeaderBar.DataContext = _tile;
        TileHeaderPopup.Width = Math.Max(0, ActualWidth);
        TileHeaderBar.Width = Math.Max(0, ActualWidth);
        if (!TileHeaderPopup.IsOpen)
        {
            TileHeaderPopup.IsOpen = true;
        }
        RestartIdleTimer();
    }

    public void HideHeader()
    {
        _headerIdleTimer?.Stop();
        if (TileHeaderPopup.IsOpen)
        {
            TileHeaderPopup.IsOpen = false;
        }
    }

    public void RestartIdleTimer()
    {
        if (!TileHeaderPopup.IsOpen) return;
        _headerIdleTimer?.Stop();
        _headerIdleTimer?.Start();
    }

    private void OnHeaderMuteClicked(object sender, RoutedEventArgs e)
    {
        if (_tile is not null && Workspace is { } ws)
        {
            ws.ToggleMuteTileCommand.Execute(_tile);
        }
    }

    private async void OnHeaderCaptureClicked(object sender, RoutedEventArgs e)
    {
        if (_tile is not null && Workspace is { } ws)
        {
            await ws.QuickCaptureCommand.ExecuteAsync(_tile);
        }
    }

    private void OnHeaderMaximizeClicked(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private async void OnHeaderStopClicked(object sender, RoutedEventArgs e)
    {
        HideHeader();
        if (_tile is not null && Workspace is { } ws)
        {
            await ws.StopTileCommand.ExecuteAsync(_tile);
        }
    }

    private void OnHeaderRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenTileContextMenu();
    }

    public void UpdateTileAspectRatio()
    {
        if (_tile is null) return;
        var mode = _tile.AspectRatio;
        string? targetRatio;
        if (string.IsNullOrEmpty(mode) || mode is "fill" or "default")
        {
            // 自适应铺满：按当前宿主窗格实际像素宽高计算比例，使 LibVLC 严格铺满，杜绝上下/左右黑边
            var w = PlayerHost.ActualWidth > 10 ? PlayerHost.ActualWidth : ActualWidth;
            var h = PlayerHost.ActualHeight > 10 ? PlayerHost.ActualHeight : ActualHeight;
            if (w > 10 && h > 10)
            {
                targetRatio = $"{(int)Math.Round(w)}:{(int)Math.Round(h)}";
            }
            else
            {
                targetRatio = null;
            }
        }
        else if (mode is "original")
        {
            targetRatio = null;
        }
        else
        {
            targetRatio = mode;
        }

        if (_lastAppliedRatio != targetRatio)
        {
            _lastAppliedRatio = targetRatio;
            _tile.ApplyDisplayRatio(targetRatio);
        }
    }

    private void AspectRatioClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string ratio && _tile is not null && Workspace is not null)
        {
            _tile.SetAspectRatio(ratio);
            _lastAppliedRatio = null;
            UpdateTileAspectRatio();
            var desc = ratio switch
            {
                "fill" or "default" => "自适应铺满",
                "original" => "原始比例",
                _ => ratio
            };
            Workspace.Status = $"窗口 {_tile.Number} 画面比例已设置为：{desc}";
        }
    }
}
