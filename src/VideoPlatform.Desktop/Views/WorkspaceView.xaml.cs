using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.Services;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private Point _dragOrigin;
    private bool _ptzPressed;
    private WorkspaceViewModel? _subscribedVm;

    private System.Windows.Threading.DispatcherTimer? _fullscreenToolbarTimer;

    public WorkspaceView()
    {
        InitializeComponent();
        InitFullscreenPopups();
        Timeline.SeekRequested += async time => { if (DataContext is WorkspaceViewModel vm) await vm.SeekAsync(time); };
        Timeline.RangeSelected += (start, end) => { if (DataContext is WorkspaceViewModel vm) vm.SetTimelineClip(start, end); };
        PreviewMouseLeftButtonUp += PtzReleased;
        Loaded += WindowLoaded;
        DataContextChanged += (_, _) =>
        {
            if (_subscribedVm is not null) _subscribedVm.PropertyChanged -= WorkspacePropertyChanged;
            _subscribedVm = DataContext as WorkspaceViewModel;
            if (_subscribedVm is not null) _subscribedVm.PropertyChanged += WorkspacePropertyChanged;
            if (_subscribedVm?.IsFullscreen != true) HideFullscreenControls();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (DataContext is WorkspaceViewModel)
            {
                MainWindow.SyncAllVideoOverlays(IsVisible);
                _ = ApplyLayoutTiersAsync();
            }
            if (!IsVisible) HideFullscreenControls();
        };
        Unloaded += async (_, _) =>
        {
            if (Window.GetWindow(this) is Window win)
            {
                win.Deactivated -= WindowDeactivated;
                win.StateChanged -= WindowStateChanged;
            }
            if (_subscribedVm is not null)
            {
                _subscribedVm.PropertyChanged -= WorkspacePropertyChanged;
                _subscribedVm = null;
            }
            VideoPlatform.Desktop.Services.VideoMouseHook.IsOverFullscreenToolbar = null;
            VideoPlatform.Desktop.Services.VideoMouseHook.MouseMoved -= OnGlobalMouseMoved;
            HideFullscreenControls();
            MainWindow.SyncAllVideoOverlays(false);
            if (DataContext is WorkspaceViewModel vm) await vm.StopPtzAsync();
        };
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        VideoPlatform.Desktop.Services.VideoMouseHook.IsOverFullscreenToolbar = IsOverFullscreenToolbar;
        VideoPlatform.Desktop.Services.VideoMouseHook.MouseMoved -= OnGlobalMouseMoved;
        VideoPlatform.Desktop.Services.VideoMouseHook.MouseMoved += OnGlobalMouseMoved;
        if (Window.GetWindow(this) is Window win)
        {
            win.Deactivated += WindowDeactivated;
            win.StateChanged += WindowStateChanged;
        }
    }

    private void WindowDeactivated(object? sender, EventArgs e) => HideFullscreenControls();

    private void WindowStateChanged(object? sender, EventArgs e) { }

    private void InitFullscreenPopups()
    {
        FullscreenBottomPopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
        {
            var y = Math.Max(0, targetSize.Height - popupSize.Height);
            return [new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(0, y), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)];
        };

        FullscreenExitPopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
        {
            var x = Math.Max(0, targetSize.Width - popupSize.Width - 16);
            return [new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(x, 12), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)];
        };

        _fullscreenToolbarTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _fullscreenToolbarTimer.Tick += (_, _) =>
        {
            _fullscreenToolbarTimer.Stop();
            HideFullscreenControls();
        };
    }

    public bool IsOverFullscreenToolbar(Point screenPt)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.IsFullscreen) return false;

        if (FullscreenBottomPopup.IsOpen)
        {
            try
            {
                var pt = FullscreenBottomBar.PointFromScreen(screenPt);
                if (pt.X >= 0 && pt.X < FullscreenBottomBar.ActualWidth && pt.Y >= 0 && pt.Y < FullscreenBottomBar.ActualHeight)
                    return true;
            }
            catch { }
        }

        if (FullscreenExitPopup.IsOpen)
        {
            try
            {
                var pt = FullscreenExitBar.PointFromScreen(screenPt);
                if (pt.X >= 0 && pt.X < FullscreenExitBar.ActualWidth && pt.Y >= 0 && pt.Y < FullscreenExitBar.ActualHeight)
                    return true;
            }
            catch { }
        }

        return false;
    }

    private void OnGlobalMouseMoved(Point screenPt)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.IsFullscreen)
        {
            if (FullscreenBottomPopup.IsOpen || FullscreenExitPopup.IsOpen)
            {
                HideFullscreenControls();
            }
            return;
        }

        Point localPt;
        try
        {
            localPt = VideoMatrixHost.PointFromScreen(screenPt);
        }
        catch
        {
            return;
        }

        var hostWidth = VideoMatrixHost.ActualWidth;
        var hostHeight = VideoMatrixHost.ActualHeight;
        if (hostWidth <= 0 || hostHeight <= 0) return;

        var isInsideHost = localPt.X >= 0 && localPt.X < hostWidth && localPt.Y >= 0 && localPt.Y < hostHeight;
        if (!isInsideHost)
        {
            HideFullscreenControls();
            return;
        }

        // 鼠标落在底端 80px 范围（或悬停在已展开的底部条上）唤出底部控制条
        var isNearBottom = localPt.Y >= hostHeight - 80 || (FullscreenBottomPopup.IsOpen && localPt.Y >= hostHeight - FullscreenBottomBar.ActualHeight);
        var isNearTopRight = localPt.Y <= 80 && localPt.X >= hostWidth - 220;

        if (isNearBottom || isNearTopRight)
        {
            ShowFullscreenControls(isNearBottom, isNearTopRight || isNearBottom);
        }
        else
        {
            HideFullscreenControls();
        }
    }

    private void ShowFullscreenControls(bool showBottom, bool showExit)
    {
        if (DataContext is not WorkspaceViewModel vm || !vm.IsFullscreen) return;

        FullscreenBottomPopup.DataContext = vm;
        FullscreenExitPopup.DataContext = vm;

        var width = VideoMatrixHost.ActualWidth;
        if (width > 0)
        {
            FullscreenBottomPopup.Width = width;
            FullscreenBottomBar.Width = width;
        }

        if (showBottom)
        {
            if (!FullscreenBottomPopup.IsOpen) FullscreenBottomPopup.IsOpen = true;
        }
        else
        {
            FullscreenBottomPopup.IsOpen = false;
        }

        if (showExit || showBottom)
        {
            if (!FullscreenExitPopup.IsOpen) FullscreenExitPopup.IsOpen = true;
        }
        else
        {
            FullscreenExitPopup.IsOpen = false;
        }

        _fullscreenToolbarTimer?.Stop();
        _fullscreenToolbarTimer?.Start();
    }

    private void HideFullscreenControls()
    {
        _fullscreenToolbarTimer?.Stop();
        if (FullscreenBottomPopup.IsOpen) FullscreenBottomPopup.IsOpen = false;
        if (FullscreenExitPopup.IsOpen) FullscreenExitPopup.IsOpen = false;
    }

    /// <summary>布局/放大/全屏变化后统一套用显示档位（格子子码流、聚焦主码流）。</summary>
    private async Task ApplyLayoutTiersAsync()
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        try { await vm.ApplyLayoutTiersAsync(); }
        catch (Exception ex) { ClientFiles.Log($"套用显示档位失败：{ex.Message}"); }
    }

    private void WorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (e.PropertyName == nameof(WorkspaceViewModel.IsFullscreen))
        {
            if (!vm.IsFullscreen) HideFullscreenControls();
        }
        // 分屏格数和布局模板切换时套用显示档位；单窗放大与全屏仅做视口几何缩放，不重新拉流
        if (e.PropertyName is nameof(WorkspaceViewModel.LayoutCount) or nameof(WorkspaceViewModel.SelectedLayoutPreset))
        {
            _ = ApplyLayoutTiersAsync();
        }
    }

    private async void MatrixBlankClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        vm.SelectedTile = null;
        await vm.StopPtzAsync();
    }

    private void ResourceSelected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (DataContext is WorkspaceViewModel vm) vm.SelectedResource = e.NewValue as ResourceNode; }
    /// <summary>点击主预览视图标签即切换分屏方案（iVMS-4200 视图切换逻辑）；程序化恢复选中不触发。</summary>
    private async void ViewTabClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (e.OriginalSource is not DependencyObject source) return;
        // 仅当鼠标落在某个标签项上才算用户点击；刷新后的选中恢复不会经过这里。
        DependencyObject? node = source;
        while (node is not null && node is not ListBoxItem && !ReferenceEquals(node, ViewTabs))
        {
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        if (node is ListBoxItem item && item.DataContext is LayoutDto layout)
        {
            vm.SelectedLayout = layout;
            await vm.ApplyLayoutCommand.ExecuteAsync(null);
        }
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
                    var targetTile = NextFreeTile(vm);
                    await vm.OpenChannelAsync(channel, targetTile);
                }
            }
            else if (!vm.IsPlayback)
            {
                var channels = selected.Flatten().Where(n => n.IsChannel && n.Channel is { Online: true }).Select(n => n.Channel!).Take(vm.LayoutCount).ToArray();
                if (channels.Length > 0) await vm.OpenChannelsBatchAsync(channels);
            }
        }
    }

    private static VideoTileViewModel? NextFreeTile(WorkspaceViewModel vm)
    {
        var target = vm.SelectedTile;
        if (target?.SessionId is not null) target = vm.VisibleTiles.FirstOrDefault(tile => tile.SessionId is null) ?? target;
        return target;
    }

    private ResourceNode? SelectedNode() => ResourceTree.SelectedItem as ResourceNode;

    private async void TreePreviewClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || SelectedNode() is not { } node) return;
        try
        {
            if (vm.IsPlayback) await vm.SetModeAsync(false);
            if (node.Channel is { } channel) await vm.OpenChannelAsync(channel, NextFreeTile(vm));
        }
        catch (Exception ex) { vm.Status = $"打开通道失败：{ex.Message}"; }
    }

    private async void TreePlaybackClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || SelectedNode() is not { } node) return;
        try
        {
            if (!vm.IsPlayback) await vm.SetModeAsync(true);
            if (node.Channel is not null) await vm.SearchRecordingsCommand.ExecuteAsync(null);
        }
        catch (Exception ex) { vm.Status = $"切换回放失败：{ex.Message}"; }
    }

    private async void TreeBatchPreviewClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || SelectedNode() is not { } node) return;
        var channels = node.Flatten().Where(n => n.IsChannel && n.Channel is { Online: true }).Select(n => n.Channel!).Take(vm.LayoutCount).ToArray();
        if (channels.Length == 0) { vm.Status = "所选分组下没有在线通道。"; return; }
        try
        {
            if (vm.IsPlayback) await vm.SetModeAsync(false);
            await vm.OpenChannelsBatchAsync(channels);
        }
        catch (Exception ex) { vm.Status = $"批量预览失败：{ex.Message}"; }
    }

    private void TreePtzClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || SelectedNode() is not { } node) return;
        vm.SelectedResource = node;
        vm.IsPtzCollapsed = false;
        if (node.Channel is { PtzCapable: false }) vm.Status = "该通道不支持云台控制。";
    }

    private void TreeExportClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || SelectedNode() is not { } node) return;
        vm.SelectedResource = node;
        vm.Status = "请在右侧检索面板选择时间段后提交导出。";
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
