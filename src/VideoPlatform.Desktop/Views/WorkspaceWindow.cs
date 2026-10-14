using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public sealed class WorkspaceWindow : Window
{
    private readonly WorkspaceViewModel _workspace;
    private readonly WorkspaceRegistry _registry;
    private bool _closed;
    private bool _closing;
    private WindowState _previousState;
    private readonly StackPanel _toolbar;
    public WorkspaceWindow(WorkspaceViewModel workspace, WorkspaceRegistry registry, WorkspacePlacement? placement = null)
    {
        _workspace = workspace;
        _registry = registry;
        DataContext = workspace;
        Title = "VisiCore · 独立值守工作区";
        Width = 1400; Height = 900; MinWidth = 800; MinHeight = 600;
        SetResourceReference(BackgroundProperty, "PageBrush");
        var panel = new DockPanel();
        var toolbar = _toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        var live = new Button { Content = "实时值守", Margin = new Thickness(4) };
        var playback = new Button { Content = "录像回查", Margin = new Thickness(4) };
        var restore = new Button { Content = "一键恢复播放", Command = workspace.RestorePlaybackCommand, Margin = new Thickness(4) };
        live.Click += async (_, _) => await workspace.SetModeAsync(false);
        playback.Click += async (_, _) => await workspace.SetModeAsync(true);
        live.SetBinding(IsEnabledProperty, nameof(workspace.CanLive));
        playback.SetBinding(IsEnabledProperty, nameof(workspace.CanPlayback));
        toolbar.Children.Add(live); toolbar.Children.Add(playback); toolbar.Children.Add(restore);
        DockPanel.SetDock(toolbar, Dock.Top); panel.Children.Add(toolbar);
        panel.Children.Add(new WorkspaceView { DataContext = workspace });
        Content = panel;
        Loaded += (_, _) => { WindowPlacement.Restore(this, placement); VideoMouseHook.Install(); };
        Deactivated += async (_, _) => await workspace.StopPtzAsync();
        PreviewMouseLeftButtonUp += async (_, _) => await workspace.StopPtzAsync();
        Closing += ClosingWindow;
        workspace.FullscreenRequested += ToggleFullscreen;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F11 || (e.Key == Key.Escape && workspace.IsFullscreen)) { workspace.FullscreenCommand.Execute(null); e.Handled = true; } };
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (message == 0x007e) Dispatcher.BeginInvoke(() => WindowPlacement.EnsureVisible(this));
                return IntPtr.Zero;
            });
    }
    private void ToggleFullscreen()
    {
        _toolbar.Visibility = _workspace.IsFullscreen ? Visibility.Collapsed : Visibility.Visible;
        if (_workspace.IsFullscreen)
        {
            _previousState = WindowState;
            WindowState = WindowState.Normal; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized;
        }
        else { WindowState = WindowState.Normal; WindowStyle = WindowStyle.SingleBorderWindow; WindowState = _previousState; }
    }
    private async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try { await _registry.RemoveAsync(_workspace); }
        finally { _workspace.FullscreenRequested -= ToggleFullscreen; _closed = true; Close(); }
    }
    public void CloseForShutdown() { _closed = true; _workspace.FullscreenRequested -= ToggleFullscreen; Close(); }
}
