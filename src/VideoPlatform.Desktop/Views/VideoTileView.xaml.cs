using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VideoPlatform.Desktop.Models;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class VideoTileView : UserControl
{
    private VideoTileViewModel? _tile;
    private LibVLCSharp.WPF.VideoView? _video;
    public VideoTileView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_tile is not null) _tile.PropertyChanged -= TileChanged;
            _tile = DataContext as VideoTileViewModel;
            if (_tile is not null) _tile.PropertyChanged += TileChanged;
            UpdatePlayer();
        };
        IsVisibleChanged += (_, _) => UpdatePlayer();
        Loaded += (_, _) => UpdatePlayer();
        Unloaded += (_, _) => ReleaseView();
    }
    private void TileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoTileViewModel.NativePlayer)) UpdatePlayer();
    }
    private void UpdatePlayer()
    {
        if (_tile?.NativePlayer is null) { ReleaseView(); return; }
        // 分屏放大和模块切换只隐藏宿主，避免播放期间重建原生窗口后失去视频输出。
        if (!IsVisible) return;
        if (_video is null)
        {
            var overlay = new Grid { Background = Brushes.Transparent, AllowDrop = true };
            overlay.MouseLeftButtonDown += Selected; overlay.Drop += Dropped;
            _video = new LibVLCSharp.WPF.VideoView { Content = overlay };
            PlayerHost.Content = _video;
        }
        _video.MediaPlayer = _tile.NativePlayer;
    }
    private void ReleaseView()
    {
        if (_video is null) return;
        _video.MediaPlayer = null; PlayerHost.Content = null; _video.Dispose(); _video = null;
    }
    private WorkspaceViewModel? Workspace
    {
        get
        {
            DependencyObject? parent = this;
            while (parent is not null) { if (parent is WorkspaceView view) return view.DataContext as WorkspaceViewModel; parent = VisualTreeHelper.GetParent(parent); }
            return null;
        }
    }
    private void Selected(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not VideoTileViewModel tile || Workspace is not { } workspace) return;
        workspace.SelectedTile = tile;
        if (e.ClickCount == 2) workspace.ToggleMaximizeCommand.Execute(null);
        e.Handled = true;
    }
    private async void Dropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(Channel)) is Channel channel && DataContext is VideoTileViewModel tile && Workspace is { } workspace) await workspace.OpenChannelAsync(channel, tile);
        e.Handled = true;
    }
}
