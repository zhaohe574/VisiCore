using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class WorkspaceView : UserControl
{
    private Point _dragOrigin;
    private bool _ptzPressed;
    public WorkspaceView()
    {
        InitializeComponent();
        Timeline.SeekRequested += async time => { if (DataContext is WorkspaceViewModel vm) await vm.SeekAsync(time); };
        PreviewMouseLeftButtonUp += PtzReleased;
        Unloaded += async (_, _) => { if (DataContext is WorkspaceViewModel vm) await vm.StopPtzAsync(); };
    }
    private void ResourceSelected(object sender, RoutedPropertyChangedEventArgs<object> e) { if (DataContext is WorkspaceViewModel vm) vm.SelectedResource = e.NewValue as ResourceNode; }
    private async void ResourceDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (ResourceTree.SelectedItem is ResourceNode { Channel: { } channel } && DataContext is WorkspaceViewModel vm)
        {
            if (vm.IsPlayback) await vm.SearchRecordingsCommand.ExecuteAsync(null);
            else await vm.OpenChannelAsync(channel);
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
