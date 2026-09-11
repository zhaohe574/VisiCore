using System.ComponentModel;
using System.Windows;
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
    private Grid? _hoverBar;
    private string? _lastAppliedRatio;

    public VideoTileView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_tile is not null) _tile.PropertyChanged -= TileChanged;
            _tile = DataContext as VideoTileViewModel;
            if (_tile is not null) _tile.PropertyChanged += TileChanged;
            _lastAppliedRatio = null;
            UpdatePlayer();
            UpdateTileAspectRatio();
        };
        IsVisibleChanged += (_, _) =>
        {
            UpdatePlayer();
            SyncOverlayVisibility();
            if (IsVisible) UpdateTileAspectRatio();
        };
        Loaded += (_, _) =>
        {
            UpdatePlayer();
            SyncOverlayVisibility();
            UpdateTileAspectRatio();
        };
        SizeChanged += (_, _) =>
        {
            UpdateTileAspectRatio();
        };
        Unloaded += (_, _) =>
        {
            SyncOverlayVisibility();
            ReleaseView();
        };
    }

    private void TileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoTileViewModel.NativePlayer))
        {
            UpdatePlayer();
            UpdateTileAspectRatio();
        }
        else if (e.PropertyName == nameof(VideoTileViewModel.IsPlaying))
        {
            if (_tile?.IsPlaying == true) UpdateTileAspectRatio();
        }
        else if (e.PropertyName == nameof(VideoTileViewModel.AspectRatio))
        {
            _lastAppliedRatio = null;
            UpdateTileAspectRatio();
        }
        else if (e.PropertyName == nameof(VideoTileViewModel.IsVisible))
        {
            SyncOverlayVisibility();
        }
    }

    public void SyncOverlayVisibility()
    {
        var visible = IsVisible && (_tile?.IsVisible ?? false);
        if (_video is not null)
        {
            _video.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (_video.Content is UIElement overlay)
            {
                overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            try
            {
                var prop = _video.GetType().GetProperty("ForegroundWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop?.GetValue(_video) is Window fw)
                {
                    fw.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { }
        }
    }

    private void UpdatePlayer()
    {
        if (_tile?.NativePlayer is null) { ReleaseView(); return; }
        // 分屏放大和模块切换只隐藏宿主，避免播放期间重建原生窗口后失去视频输出。
        if (!IsVisible) { SyncOverlayVisibility(); return; }
        if (_video is null)
        {
            // 关键：Background 设置为几乎不可见但逻辑实色的画刷（Alpha=1，即 #01000000），
            // 使得底层 Win32 原生视频窗口上的所有鼠标事件（单击、双击、右键、拖拽）100% 被 WPF 捕获！
            var overlay = new Grid
            {
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                AllowDrop = true
            };
            overlay.PreviewMouseLeftButtonDown += OverlayMouseLeftDown;
            overlay.PreviewMouseRightButtonDown += OverlayMouseRightDown;
            overlay.Drop += Dropped;
            overlay.ContextMenu = this.ContextMenu;
            overlay.Tag = this.Tag;
            overlay.DataContext = this.DataContext;

            // 顶部常驻微型 OSD 水印（通道名称与高清角标）
            var osdBar = BuildOsdBar();
            overlay.Children.Add(osdBar);

            // 悬浮工具栏
            var hoverBar = BuildHoverBar();
            hoverBar.Visibility = Visibility.Collapsed;
            overlay.Children.Add(hoverBar);
            overlay.MouseEnter += (_, _) => { if (_tile?.SessionId is not null) hoverBar.Visibility = Visibility.Visible; };
            overlay.MouseLeave += (_, _) => hoverBar.Visibility = Visibility.Collapsed;
            overlay.PreviewMouseMove += (_, e) =>
            {
                if (WorkspaceViewInstance is { } wv)
                {
                    try
                    {
                        var screenPt = overlay.PointToScreen(e.GetPosition(overlay));
                        wv.NotifyScreenMouseMove(screenPt);
                    }
                    catch { }
                }
            };
            _hoverBar = hoverBar;

            // 右下角窗口编号角标（iVMS-4200 风格“窗口 01”标识，播放时可见）
            var numberPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 16, 18, 22)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(6, 2, 6, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 36),
                IsHitTestVisible = false
            };
            var numberText = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xA5, 0xB0)),
                VerticalAlignment = VerticalAlignment.Center
            };
            numberText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(VideoTileViewModel.Number)) { StringFormat = "窗口 {0:D2}" });
            numberPill.Child = numberText;
            numberPill.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(VideoTileViewModel.IsPlaying))
            {
                Converter = Application.Current.TryFindResource("BoolVisibility") as System.Windows.Data.IValueConverter
            });
            overlay.Children.Add(numberPill);

            _video = new LibVLCSharp.WPF.VideoView
            {
                Content = overlay,
                Background = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _video.Loaded += (_, _) =>
            {
                try
                {
                    if (Application.Current.MainWindow is { } win)
                    {
                        var handle = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                        MainWindow.EnsureChildWindowsBlack(handle);
                    }
                }
                catch { }
            };
            PlayerHost.Content = _video;
        }
        else if (_video.Content is Grid overlay)
        {
            overlay.Tag = this.Tag;
            overlay.DataContext = this.DataContext;
        }
        _video.MediaPlayer = _tile.NativePlayer;
        SyncOverlayVisibility();
    }

    private Grid BuildOsdBar()
    {
        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 6, 6, 0),
            IsHitTestVisible = false
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 左侧通道名称与绿灯微型药丸
        var leftPill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 16, 18, 22)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var leftPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromRgb(0x43, 0xB5, 0x81)),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleBlock = new TextBlock
        {
            FontSize = 11,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200
        };
        titleBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(VideoTileViewModel.Title)));
        leftPanel.Children.Add(dot);
        leftPanel.Children.Add(titleBlock);
        leftPill.Child = leftPanel;
        Grid.SetColumn(leftPill, 0);
        grid.Children.Add(leftPill);

        // 右侧高清码流角标
        var badge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 48, 52, 60)),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(5, 2, 5, 2),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var badgeText = new TextBlock
        {
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        badgeText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(VideoTileViewModel.StreamBadge)));
        badge.Child = badgeText;
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        return grid;
    }

    private Grid BuildHoverBar()
    {
        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(210, 20, 22, 26)),
            Height = 30
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            Foreground = Brushes.LightGray,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        titleBlock.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(VideoTileViewModel.Title)));
        Grid.SetColumn(titleBlock, 0);
        grid.Children.Add(titleBlock);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        Button CreateBtn(string icon, string tooltip, Action onClick)
        {
            var btn = new Button
            {
                Content = icon,
                ToolTip = tooltip,
                Style = TryFindResource("IconButton") as Style,
                Width = 26,
                Height = 24,
                FontSize = 12,
                Margin = new Thickness(2, 0, 2, 0)
            };
            btn.Click += (_, e) => { onClick(); e.Handled = true; };
            return btn;
        }

        actions.Children.Add(CreateBtn("\uE722", "一键抓图", () =>
        {
            if (_tile is not null && Workspace is not null) Workspace.QuickCaptureCommand.Execute(_tile);
        }));

        actions.Children.Add(CreateBtn("\uE767", "开启/关闭声音", () =>
        {
            if (_tile is not null && Workspace is not null) Workspace.ToggleMuteTileCommand.Execute(_tile);
        }));

        actions.Children.Add(CreateBtn("\uE740", "单窗放大/还原", () =>
        {
            if (Workspace is not null)
            {
                if (_tile is not null) Workspace.SelectedTile = _tile;
                Workspace.ToggleMaximizeCommand.Execute(null);
            }
        }));

        actions.Children.Add(CreateBtn("\uE711", "停止预览", () =>
        {
            if (_tile is not null && Workspace is not null) Workspace.StopTileCommand.Execute(_tile);
        }));

        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private void ReleaseView()
    {
        _lastAppliedRatio = null;
        if (_video is null) return;
        try
        {
            var prop = _video.GetType().GetProperty("ForegroundWindow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop?.GetValue(_video) is Window fw)
            {
                fw.Visibility = Visibility.Collapsed;
                fw.Close();
            }
        }
        catch { }
        _video.MediaPlayer = null; PlayerHost.Content = null; _video.Dispose(); _video = null; _hoverBar = null;
    }

    private WorkspaceViewModel? Workspace => WorkspaceViewInstance?.DataContext as WorkspaceViewModel;

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

    private void TileMouseLeftDown(object sender, MouseButtonEventArgs e) => HandleLeftDown(e);

    private void TileMouseRightDown(object sender, MouseButtonEventArgs e) => HandleRightDown(e);

    private void OverlayMouseLeftDown(object sender, MouseButtonEventArgs e) => HandleLeftDown(e);

    private void OverlayMouseRightDown(object sender, MouseButtonEventArgs e) => HandleRightDown(e);

    private void HandleLeftDown(MouseButtonEventArgs e)
    {
        if (DataContext is not VideoTileViewModel tile || Workspace is not { } workspace) return;
        workspace.SelectedTile = tile;
        if (e.ClickCount == 2)
        {
            workspace.ToggleMaximizeCommand.Execute(null);
            e.Handled = true;
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
        if (_tile is not null && Workspace is not null)
        {
            try
            {
                await _tile.SwitchStreamAsync(1);
                Workspace.Status = $"窗口 {_tile.Number} 已切换为主码流。";
            }
            catch (Exception ex)
            {
                Workspace.Status = $"窗口 {_tile.Number} 切换主码流失败：{ex.Message}";
            }
        }
    }

    private async void SwitchToSubStreamClicked(object sender, RoutedEventArgs e)
    {
        if (_tile is not null && Workspace is not null)
        {
            try
            {
                await _tile.SwitchStreamAsync(2);
                Workspace.Status = $"窗口 {_tile.Number} 已切换为子码流。";
            }
            catch (Exception ex)
            {
                Workspace.Status = $"窗口 {_tile.Number} 切换子码流失败：{ex.Message}";
            }
        }
    }

    public void UpdateTileAspectRatio()
    {
        if (_tile is null) return;
        var mode = _tile.AspectRatio;
        string? targetRatio;
        if (string.IsNullOrEmpty(mode) || mode is "fill" or "default")
        {
            var w = (int)Math.Round(ActualWidth);
            var h = (int)Math.Round(ActualHeight);
            if (w <= 10 || h <= 10) return;
            targetRatio = $"{w}:{h}";
        }
        else if (mode == "original")
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
