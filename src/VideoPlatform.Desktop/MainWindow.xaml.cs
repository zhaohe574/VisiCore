using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop;

public partial class MainWindow : Window
{
    private const int WM_CTLCOLORSTATIC = 0x0138;
    private const int WM_CTLCOLORDLG = 0x0136;
    private const int WM_ERASEBKGND = 0x0014;
    private static readonly IntPtr BlackBrush = GetStockObject(4); // BLACK_BRUSH = 4

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    public static void EnsureChildWindowsBlack(IntPtr parentHwnd)
    {
        if (parentHwnd == IntPtr.Zero) return;
        try
        {
            EnumChildWindows(parentHwnd, (childHwnd, _) =>
            {
                var sb = new System.Text.StringBuilder(128);
                GetClassName(childHwnd, sb, sb.Capacity);
                if (string.Equals(sb.ToString(), "static", StringComparison.OrdinalIgnoreCase))
                {
                    const int GCLP_HBRBACKGROUND = -10;
                    if (IntPtr.Size == 8)
                        SetClassLongPtr64(childHwnd, GCLP_HBRBACKGROUND, BlackBrush);
                    else
                        SetClassLong32(childHwnd, GCLP_HBRBACKGROUND, (int)BlackBrush);
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    public static void SyncAllVideoOverlays(bool visible)
    {
        try
        {
            foreach (Window win in Application.Current.Windows)
            {
                if (win.GetType().Name.Contains("ForegroundWindow", StringComparison.OrdinalIgnoreCase))
                {
                    win.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
        catch { }
    }

    private readonly ShellViewModel _viewModel;
    private bool _closed;
    private bool _closing;
    private WindowState _previousState;
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent(); DataContext = _viewModel = viewModel;
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        viewModel.Workspace.FullscreenRequested += ToggleFullscreen;
        viewModel.Workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceViewModel.IsFullscreen))
            {
                UpdateFullscreenRows(_viewModel.Workspace.IsFullscreen);
            }
        };
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.Module))
            {
                bool isMedia = _viewModel.Module is "live" or "playback";
                SyncAllVideoOverlays(isMedia);
            }
        };
        // 用户中心按钮左键弹出下拉菜单（iVMS-4200 顶部用户菜单逻辑；不在 XAML 挂事件以免布局测试解析失败）
        UserMenuButton.Click += (_, _) =>
        {
            if (UserMenuButton.ContextMenu is not { } menu) return;
            menu.PlacementTarget = UserMenuButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        MaximizeButton.Click += (_, _) =>
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        };
        CloseButton.Click += (_, _) => Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        bool isMax = WindowState == WindowState.Maximized;
        if (MaximizeButton != null)
        {
            MaximizeButton.Content = isMax ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = isMax ? "向下还原" : "最大化";
        }
        if (ShellViewport != null)
        {
            ShellViewport.Margin = (isMax && !_viewModel.Workspace.IsFullscreen) ? new Thickness(7) : new Thickness(0);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 彻底消除 Win32 static 原生窗口及底层对话框在重绘或首帧渲染前的白色背景（Windows 默认返回 COLOR_WINDOW 白色画刷）
        if (msg == WM_CTLCOLORSTATIC || msg == WM_CTLCOLORDLG)
        {
            handled = true;
            return BlackBrush;
        }
        return IntPtr.Zero;
    }
    private async void LoadedWindow(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();
    private async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        ShowInTaskbar = false;
        Hide();
        SyncAllVideoOverlays(false);
        try { await _viewModel.CloseAsync(); }
        catch { }
        finally { _closed = true; Close(); Application.Current?.Shutdown(); }
    }
    private async void DeactivatedWindow(object? sender, EventArgs e) => await _viewModel.Workspace.StopPtzAsync();
    private async void ReleasedPointer(object sender, MouseButtonEventArgs e) => await _viewModel.Workspace.StopPtzAsync();
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.Escape && _viewModel.Workspace.IsFullscreen))
        {
            _viewModel.Workspace.FullscreenCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _viewModel.Module == "settings")
        {
            _viewModel.CloseSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }
    private WindowChrome? _savedChrome;
    private ResizeMode _previousResizeMode = ResizeMode.CanResize;
    private void UpdateFullscreenRows(bool isFullscreen)
    {
        if (isFullscreen)
        {
            if (TopTitleBar != null) TopTitleBar.Visibility = Visibility.Collapsed;
            TitleBarRow.Height = new GridLength(0);
            NavBarRow.Height = new GridLength(0);
            StatusBarRow.Height = new GridLength(0);
            ShellViewport.Margin = new Thickness(0);
        }
        else
        {
            if (TopTitleBar != null) TopTitleBar.Visibility = Visibility.Visible;
            TitleBarRow.Height = GridLength.Auto;
            NavBarRow.Height = new GridLength(0);
            StatusBarRow.Height = GridLength.Auto;
            ShellViewport.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        }
    }

    private void ToggleFullscreen()
    {
        if (!_viewModel.Workspace.IsFullscreen)
        {
            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = _previousResizeMode;
            if (_savedChrome is not null)
            {
                WindowChrome.SetWindowChrome(this, _savedChrome);
            }
            UpdateFullscreenRows(false);
            WindowState = WindowState.Normal;
            if (_previousState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        else
        {
            _savedChrome ??= WindowChrome.GetWindowChrome(this);
            _previousState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            _previousResizeMode = ResizeMode;
            WindowChrome.SetWindowChrome(this, null);
            UpdateFullscreenRows(true);
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowState = WindowState.Maximized;
        }
    }
}
