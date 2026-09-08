using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop;

public partial class MainWindow : Window
{
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
    }
    private async void LoadedWindow(object sender, RoutedEventArgs e) => await _viewModel.InitializeAsync();
    private async void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true; IsEnabled = false;
        try { await _viewModel.CloseAsync(); }
        finally { _closed = true; Close(); }
    }
    private async void DeactivatedWindow(object? sender, EventArgs e) => await _viewModel.Workspace.StopPtzAsync();
    private async void ReleasedPointer(object sender, MouseButtonEventArgs e) => await _viewModel.Workspace.StopPtzAsync();
    private void KeyPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 || e.Key == Key.Escape && WindowStyle == WindowStyle.None) { ToggleFullscreen(); e.Handled = true; }
    }
    private void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None) { WindowStyle = WindowStyle.SingleBorderWindow; WindowState = _previousState; }
        else { _previousState = WindowState; WindowStyle = WindowStyle.None; WindowState = WindowState.Maximized; }
    }
}
