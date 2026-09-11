using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private Point _dragOrigin;
    private bool _ptzPressed;
    private DispatcherTimer? _bottomBarTimer;
    private WorkspaceViewModel? _subscribedVm;

    public WorkspaceView()
    {
        InitializeComponent();
        FloatingToolbarPopup.CustomPopupPlacementCallback = PlaceFloatingToolbar;
        FloatingExitFullscreenPopup.CustomPopupPlacementCallback = PlaceFloatingExitButton;
        VideoMatrixHost.SizeChanged += (_, _) => UpdateFloatingPopups();
        SetupBottomBarAutoFade();
        Timeline.SeekRequested += async time => { if (DataContext is WorkspaceViewModel vm) await vm.SeekAsync(time); };
        PreviewMouseLeftButtonDown += WorkspacePreviewMouseDown;
        PreviewMouseLeftButtonUp += PtzReleased;
        Loaded += WindowLoaded;
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm is not null) _subscribedVm.PropertyChanged -= WorkspacePropertyChanged;
            _subscribedVm = DataContext as WorkspaceViewModel;
            if (_subscribedVm is not null) _subscribedVm.PropertyChanged += WorkspacePropertyChanged;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (DataContext is WorkspaceViewModel)
            {
                MainWindow.SyncAllVideoOverlays(IsVisible);
            }
        };
        Unloaded += async (_, _) =>
        {
            _bottomBarTimer?.Stop();
            FloatingToolbarPopup.IsOpen = false;
            FloatingExitFullscreenPopup.IsOpen = false;
            if (Window.GetWindow(this) is Window win)
            {
                win.LocationChanged -= WindowLocationOrSizeChanged;
                win.SizeChanged -= WindowLocationOrSizeChanged;
                win.Deactivated -= WindowDeactivated;
                win.StateChanged -= WindowStateChanged;
            }
            if (_subscribedVm is not null)
            {
                _subscribedVm.PropertyChanged -= WorkspacePropertyChanged;
                _subscribedVm = null;
            }
            MainWindow.SyncAllVideoOverlays(false);
            if (DataContext is WorkspaceViewModel vm) await vm.StopPtzAsync();
        };
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is Window win)
        {
            win.LocationChanged += WindowLocationOrSizeChanged;
            win.SizeChanged += WindowLocationOrSizeChanged;
            win.Deactivated += WindowDeactivated;
            win.StateChanged += WindowStateChanged;
        }
    }

    private void WindowLocationOrSizeChanged(object? sender, EventArgs e) => UpdateFloatingPopups();

    private void WindowDeactivated(object? sender, EventArgs e)
    {
        if (FloatingToolbarPopup.IsOpen) FloatingToolbarPopup.IsOpen = false;
    }

    private void WindowStateChanged(object? sender, EventArgs e)
    {
        if (Window.GetWindow(this) is Window win && win.WindowState == WindowState.Minimized)
        {
            if (FloatingToolbarPopup.IsOpen) FloatingToolbarPopup.IsOpen = false;
        }
    }

    private CustomPopupPlacement[] PlaceFloatingToolbar(Size popupSize, Size targetSize, Point offset)
    {
        var y = Math.Max(0, targetSize.Height - 38);
        return new[] { new CustomPopupPlacement(new Point(0, y), PopupPrimaryAxis.Horizontal) };
    }

    private CustomPopupPlacement[] PlaceFloatingExitButton(Size popupSize, Size targetSize, Point offset)
    {
        var x = Math.Max(0, targetSize.Width - popupSize.Width - 16);
        return new[] { new CustomPopupPlacement(new Point(x, 12), PopupPrimaryAxis.Horizontal) };
    }

    private void UpdateFloatingPopups()
    {
        if (FloatingToolbarPopup.IsOpen)
        {
            var offset = FloatingToolbarPopup.HorizontalOffset;
            FloatingToolbarPopup.HorizontalOffset = offset + 0.001;
            FloatingToolbarPopup.HorizontalOffset = offset;
        }
        if (FloatingExitFullscreenPopup.IsOpen)
        {
            var offset = FloatingExitFullscreenPopup.HorizontalOffset;
            FloatingExitFullscreenPopup.HorizontalOffset = offset + 0.001;
            FloatingExitFullscreenPopup.HorizontalOffset = offset;
        }
    }

    private bool IsMouseOverBottomToolbar =>
        (FloatingToolbarPopup.IsOpen && (FloatingToolbarContent.IsMouseOver || FloatingToolbarContent.IsKeyboardFocusWithin)) ||
        (BottomToolbar.IsVisible && BottomToolbar.IsMouseOver);

    private void SetupBottomBarAutoFade()
    {
        _bottomBarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        _bottomBarTimer.Tick += (_, _) =>
        {
            if (DataContext is not WorkspaceViewModel vm) return;
            if (vm.IsFullscreen && !IsMouseOverBottomToolbar)
            {
                vm.IsBottomBarHovered = false;
                FloatingToolbarPopup.IsOpen = false;
                _bottomBarTimer?.Stop();
            }
        };

        BottomToolbar.MouseEnter += (_, _) =>
        {
            _bottomBarTimer?.Stop();
            if (DataContext is WorkspaceViewModel vm) vm.IsBottomBarHovered = true;
        };

        BottomToolbar.MouseLeave += (_, _) =>
        {
            if (DataContext is WorkspaceViewModel vm && vm.IsFullscreen)
            {
                _bottomBarTimer?.Stop();
                _bottomBarTimer?.Start();
            }
        };
    }

    private void FloatingToolbarMouseEnter(object sender, MouseEventArgs e)
    {
        _bottomBarTimer?.Stop();
        if (DataContext is WorkspaceViewModel vm) vm.IsBottomBarHovered = true;
    }

    private void FloatingToolbarMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm && vm.IsFullscreen)
        {
            _bottomBarTimer?.Stop();
            _bottomBarTimer?.Start();
        }
    }

    private void WorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.IsFullscreen))
        {
            _bottomBarTimer?.Stop();
            if (DataContext is WorkspaceViewModel vm)
            {
                if (!vm.IsFullscreen)
                {
                    vm.IsBottomBarHovered = false;
                    FloatingToolbarPopup.IsOpen = false;
                }
            }
            UpdateFloatingPopups();
        }
    }

    public void NotifyScreenMouseMove(Point screenPoint)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.IsFullscreen) return;
        try
        {
            var hostPt = VideoMatrixHost.PointFromScreen(screenPoint);
            if (hostPt.Y >= VideoMatrixHost.ActualHeight - 55 && hostPt.Y <= VideoMatrixHost.ActualHeight + 10)
            {
                _bottomBarTimer?.Stop();
                vm.IsBottomBarHovered = true;
                if (!FloatingToolbarPopup.IsOpen)
                {
                    FloatingToolbarPopup.IsOpen = true;
                    UpdateFloatingPopups();
                }
            }
            else if (vm.IsBottomBarHovered && !IsMouseOverBottomToolbar)
            {
                if (_bottomBarTimer is not null && !_bottomBarTimer.IsEnabled)
                {
                    _bottomBarTimer.Start();
                }
            }
        }
        catch { }
    }

    private void VideoMatrixHostMouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.IsFullscreen) return;
        var pos = e.GetPosition(VideoMatrixHost);
        if (pos.Y >= VideoMatrixHost.ActualHeight - 55)
        {
            _bottomBarTimer?.Stop();
            vm.IsBottomBarHovered = true;
            if (!FloatingToolbarPopup.IsOpen)
            {
                FloatingToolbarPopup.IsOpen = true;
                UpdateFloatingPopups();
            }
        }
        else if (vm.IsBottomBarHovered && !IsMouseOverBottomToolbar)
        {
            if (_bottomBarTimer is not null && !_bottomBarTimer.IsEnabled)
            {
                _bottomBarTimer.Start();
            }
        }
    }

    private void VideoMatrixHostMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.IsFullscreen) return;
        if (vm.IsBottomBarHovered && !IsMouseOverBottomToolbar)
        {
            if (_bottomBarTimer is not null && !_bottomBarTimer.IsEnabled)
            {
                _bottomBarTimer.Start();
            }
        }
    }

    private void WorkspacePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (e.OriginalSource is not DependencyObject source) return;

        // 判断点击是否落在视频画面窗格内，或者点击了需要维持焦点的操作控件（PTZ、工具栏按钮、滑块、下拉框等）
        DependencyObject? curr = source;
        bool isInsideTile = false;
        bool isActionControl = false;

        while (curr is not null && !ReferenceEquals(curr, this))
        {
            if (curr is VideoTileView) { isInsideTile = true; break; }
            if (curr is Button || curr is Slider || curr is ComboBox || curr is TextBox || curr is ContextMenu || curr is MenuItem) { isActionControl = true; break; }
            curr = VisualTreeHelper.GetParent(curr);
        }

        // 点击画面之外的空白处（如视频墙空白底板、分屏网格间隙、左侧资源树空白区域等），选中的高亮框立即消失
        if (!isInsideTile && !isActionControl)
        {
            vm.SelectedTile = null;
        }
    }
    private void ResourceSelected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (DataContext is WorkspaceViewModel vm) vm.SelectedResource = e.NewValue as ResourceNode; }
    /// <summary>点击主预览视图标签即切换分屏方案（iVMS-4200 视图切换逻辑）；程序化恢复选中不触发。</summary>
    private async void ViewTabClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (e.OriginalSource is not DependencyObject source) return;
        // 仅当鼠标落在某个标签项上才算用户点击；刷新后的选中恢复不会经过这里。
        DependencyObject? node = source;
        while (node is not null && node is not ListBoxItem && !ReferenceEquals(node, ViewTabs)) node = VisualTreeHelper.GetParent(node);
        if (node is ListBoxItem && vm.SelectedLayout is not null) await vm.ApplyLayoutCommand.ExecuteAsync(null);
    }
    private async void ResourceDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (ResourceTree.SelectedItem is ResourceNode selected && DataContext is WorkspaceViewModel vm)
        {
            if (selected.Channel is { } channel)
            {
                if (vm.IsPlayback) await vm.SearchRecordingsCommand.ExecuteAsync(null);
                else
                {
                    var targetTile = vm.SelectedTile;
                    if (targetTile?.SessionId is not null)
                    {
                        var emptyTile = vm.VisibleTiles.FirstOrDefault(t => t.SessionId is null);
                        if (emptyTile is not null) targetTile = emptyTile;
                    }
                    await vm.OpenChannelAsync(channel, targetTile);
                }
            }
            else if (!vm.IsPlayback)
            {
                var channels = selected.Flatten().Where(n => n.IsChannel && n.Channel is { Online: true }).Select(n => n.Channel!).Take(vm.LayoutCount).ToArray();
                if (channels.Length > 0)
                {
                    await vm.OpenChannelsBatchAsync(channels);
                }
            }
        }
    }
    private void DragStarted(object sender, MouseButtonEventArgs e) => _dragOrigin = e.GetPosition(ResourceTree);
    private void DragMoved(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ResourceTree.SelectedItem is not ResourceNode { Channel: { } channel }) return;
        var point = e.GetPosition(ResourceTree);
        if (Math.Abs(point.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ResourceTree, new DataObject(typeof(Channel), channel), DragDropEffects.Copy);
    }
    private async void PtzPressed(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || sender is not Button { Tag: string command } button || _ptzPressed) return;
        _ptzPressed = true; button.CaptureMouse(); e.Handled = true;
        await vm.StartPtzAsync(command);
        if (!_ptzPressed || Mouse.LeftButton != MouseButtonState.Pressed) await vm.StopPtzAsync();
    }
    private async void PtzReleased(object sender, RoutedEventArgs e)
    {
        if (!_ptzPressed) return;
        _ptzPressed = false; Mouse.Capture(null);
        if (DataContext is WorkspaceViewModel vm) await vm.StopPtzAsync();
    }
}
