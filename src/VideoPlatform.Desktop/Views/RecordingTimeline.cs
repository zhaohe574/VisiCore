using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Views;

/// <summary>
/// 专业安防级录像时间轴控件：
/// - 滚轮中心缩放（24小时 ~ 1分钟）
/// - 右键/中键拖拽平移视窗
/// - 播放头拖动擦洗（Scrubbing）+ 磁性吸附附近录像边缘 + 实时时刻气泡
/// - 多通道分轨并列显示（Multi-Track）+ 激活通道高亮
/// - Shift + 鼠标拖拽框选录像区间（用于一键导出）
/// - 双击恢复全览范围
/// </summary>
public sealed class RecordingTimeline : FrameworkElement
{
    private static readonly Brush TrackBgBrush = Frozen(Color.FromRgb(24, 27, 31));
    private static readonly Brush TrackLaneBrush = Frozen(Color.FromRgb(36, 40, 46));
    private static readonly Brush ActiveLaneBrush = Frozen(Color.FromRgb(48, 56, 66));
    private static readonly Brush SegmentBrush = Frozen(Color.FromRgb(46, 163, 109));
    private static readonly Brush SelectedRangeBrush = Frozen(Color.FromArgb(90, 30, 110, 235));
    private static readonly Pen SelectedRangeBorderPen = FrozenPen(Color.FromRgb(60, 130, 245), 1.5);
    private static readonly Pen TickMajorPen = FrozenPen(Color.FromRgb(180, 190, 202), 1);
    private static readonly Pen TickMinorPen = FrozenPen(Color.FromRgb(90, 98, 108), 1);
    private static readonly Pen PlayheadPen = FrozenPen(Color.FromRgb(230, 126, 34), 2);
    private static readonly Brush PlayheadHeadBrush = Frozen(Color.FromRgb(230, 126, 34));
    private static readonly Typeface TimeTypeface = new("Microsoft YaHei UI");
    private static readonly Brush TimeBrush = Frozen(Color.FromRgb(214, 219, 226));
    private static readonly Brush TrackLabelBrush = Frozen(Color.FromRgb(150, 160, 172));
    private static readonly Brush TrackLabelActiveBrush = Frozen(Color.FromRgb(255, 255, 255));
    private static readonly Dictionary<string, FormattedText> TextCache = [];
    private static readonly object TextCacheGate = new();

    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(DateTimeOffset), typeof(RecordingTimeline),
        new FrameworkPropertyMetadata(DateTimeOffset.Now.AddHours(-1), FrameworkPropertyMetadataOptions.AffectsRender, OnRangeBoundsChanged));

    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(
        nameof(End), typeof(DateTimeOffset), typeof(RecordingTimeline),
        new FrameworkPropertyMetadata(DateTimeOffset.Now, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeBoundsChanged));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(DateTimeOffset?), typeof(RecordingTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(RecordingSegment[]), typeof(RecordingTimeline),
        new FrameworkPropertyMetadata(Array.Empty<RecordingSegment>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TracksProperty = DependencyProperty.Register(
        nameof(Tracks), typeof(TimelineTrack[]), typeof(RecordingTimeline),
        new FrameworkPropertyMetadata(Array.Empty<TimelineTrack>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedRangeStartProperty = DependencyProperty.Register(
        nameof(SelectedRangeStart), typeof(DateTimeOffset?), typeof(RecordingTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedRangeEndProperty = DependencyProperty.Register(
        nameof(SelectedRangeEnd), typeof(DateTimeOffset?), typeof(RecordingTimeline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public DateTimeOffset Start { get => (DateTimeOffset)GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public DateTimeOffset End { get => (DateTimeOffset)GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public DateTimeOffset? Position { get => (DateTimeOffset?)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public RecordingSegment[] Segments { get => (RecordingSegment[])GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public TimelineTrack[] Tracks { get => (TimelineTrack[])GetValue(TracksProperty); set => SetValue(TracksProperty, value); }
    public DateTimeOffset? SelectedRangeStart { get => (DateTimeOffset?)GetValue(SelectedRangeStartProperty); set => SetValue(SelectedRangeStartProperty, value); }
    public DateTimeOffset? SelectedRangeEnd { get => (DateTimeOffset?)GetValue(SelectedRangeEndProperty); set => SetValue(SelectedRangeEndProperty, value); }

    public event Action<DateTimeOffset>? SeekRequested;
    public event Action<DateTimeOffset, DateTimeOffset>? RangeSelected;

    private DateTimeOffset? _visibleStart;
    private DateTimeOffset? _visibleEnd;
    private bool _isDraggingPlayhead;
    private DateTimeOffset _dragPosition;
    private bool _isPanning;
    private Point _panStartPoint;
    private (DateTimeOffset Start, DateTimeOffset End) _panStartRange;
    private bool _isSelectingRange;
    private DateTimeOffset _selectionAnchor;
    private string? _lastTooltip;

    public DateTimeOffset ViewStart => _visibleStart.HasValue && _visibleStart.Value >= Start && _visibleStart.Value < End ? _visibleStart.Value : Start;
    public DateTimeOffset ViewEnd => _visibleEnd.HasValue && _visibleEnd.Value > Start && _visibleEnd.Value <= End && _visibleEnd.Value > ViewStart ? _visibleEnd.Value : End;

    public RecordingTimeline()
    {
        MinHeight = 62;
        Cursor = Cursors.Hand;
        Focusable = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    private static void OnRangeBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RecordingTimeline timeline)
        {
            timeline._visibleStart = null;
            timeline._visibleEnd = null;
        }
    }

    public static DateTimeOffset PositionAt(DateTimeOffset start, DateTimeOffset end, double fraction) =>
        start.AddTicks((long)((end - start).Ticks * Math.Clamp(fraction, 0, 1)));

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 2 || height < 10) return;

        drawing.DrawRectangle(TrackBgBrush, null, new Rect(0, 0, width, height));
        if (End <= Start) return;

        var vStart = ViewStart;
        var vEnd = ViewEnd;
        var viewDuration = (vEnd - vStart).TotalSeconds;
        if (viewDuration <= 0) return;

        double X(DateTimeOffset time) => Math.Clamp((time - vStart).TotalSeconds / viewDuration, 0, 1) * width;

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var rulerHeight = 24.0;

        // 1. 绘制顶栏时间刻度标尺
        DrawRuler(drawing, width, rulerHeight, vStart, vEnd, viewDuration, pixelsPerDip);

        // 2. 绘制分轨或单轨录像段
        var contentTop = rulerHeight + 2;
        var contentHeight = Math.Max(20, height - contentTop - 2);

        var activeTracks = Tracks;
        if (activeTracks is { Length: > 1 })
        {
            var trackHeight = contentHeight / activeTracks.Length;
            for (var i = 0; i < activeTracks.Length; i++)
            {
                var track = activeTracks[i];
                var laneTop = contentTop + i * trackHeight;
                var laneRect = new Rect(0, laneTop, width, trackHeight - 1);
                drawing.DrawRectangle(track.IsActive ? ActiveLaneBrush : TrackLaneBrush, null, laneRect);

                // 轨道通道名标签
                if (!string.IsNullOrWhiteSpace(track.ChannelName))
                {
                    var trackLabel = CachedText(track.ChannelName, 9, track.IsActive ? TrackLabelActiveBrush : TrackLabelBrush, pixelsPerDip);
                    drawing.DrawText(trackLabel, new Point(6, laneTop + Math.Max(1, (trackHeight - trackLabel.Height) / 2)));
                }

                // 录像色块
                foreach (var seg in track.Segments ?? [])
                {
                    if (seg.End <= vStart || seg.Start >= vEnd) continue;
                    var segX = X(seg.Start);
                    var segW = Math.Max(1.5, X(seg.End) - segX);
                    drawing.DrawRectangle(SegmentBrush, null, new Rect(segX, laneTop + 1, segW, trackHeight - 3));
                }
            }
        }
        else
        {
            // 单轨模式
            var laneRect = new Rect(0, contentTop, width, contentHeight);
            drawing.DrawRectangle(TrackLaneBrush, null, laneRect);

            var segs = (activeTracks is { Length: 1 } ? activeTracks[0].Segments : Segments) ?? [];
            foreach (var seg in segs)
            {
                if (seg.End <= vStart || seg.Start >= vEnd) continue;
                var segX = X(seg.Start);
                var segW = Math.Max(1.5, X(seg.End) - segX);
                drawing.DrawRectangle(SegmentBrush, null, new Rect(segX, contentTop + 1, segW, contentHeight - 2));
            }
        }

        // 3. 绘制框选区间高亮（用于快速剪辑导出）
        if (SelectedRangeStart.HasValue && SelectedRangeEnd.HasValue)
        {
            var rStart = SelectedRangeStart.Value < SelectedRangeEnd.Value ? SelectedRangeStart.Value : SelectedRangeEnd.Value;
            var rEnd = SelectedRangeStart.Value < SelectedRangeEnd.Value ? SelectedRangeEnd.Value : SelectedRangeStart.Value;
            if (rEnd > vStart && rStart < vEnd)
            {
                var selX = X(rStart);
                var selW = Math.Max(2, X(rEnd) - selX);
                var selRect = new Rect(selX, contentTop, selW, contentHeight);
                drawing.DrawRectangle(SelectedRangeBrush, SelectedRangeBorderPen, selRect);

                var spanSec = (rEnd - rStart).TotalSeconds;
                var spanText = CachedText($"已选 {TimeSpan.FromSeconds(spanSec):mm\\:ss}", 10, TimeBrush, pixelsPerDip);
                drawing.DrawText(spanText, new Point(Math.Clamp(selX + 4, 4, Math.Max(4, width - spanText.Width - 4)), contentTop + 4));
            }
        }

        // 4. 绘制播放头（带倒三角指示指针）
        var playheadTime = _isDraggingPlayhead ? _dragPosition : Position;
        if (playheadTime is { } pos && pos >= vStart && pos <= vEnd)
        {
            var px = X(pos);
            drawing.DrawLine(PlayheadPen, new Point(px, rulerHeight - 2), new Point(px, height));

            // 倒三角标记
            var triangle = new PathGeometry();
            var figure = new PathFigure { StartPoint = new Point(px - 5, rulerHeight - 8), IsClosed = true };
            figure.Segments.Add(new LineSegment(new Point(px + 5, rulerHeight - 8), false));
            figure.Segments.Add(new LineSegment(new Point(px, rulerHeight + 1), false));
            triangle.Figures.Add(figure);
            triangle.Freeze();
            drawing.DrawGeometry(PlayheadHeadBrush, null, triangle);
        }
    }

    private void DrawRuler(DrawingContext drawing, double width, double rulerHeight, DateTimeOffset vStart, DateTimeOffset vEnd, double viewDuration, double pixelsPerDip)
    {
        // 根据当前视窗跨度决定刻度密度与文本格式
        int majorIntervalSec;
        string timeFormat;

        if (viewDuration > 12 * 3600) { majorIntervalSec = 2 * 3600; timeFormat = "HH:00"; }
        else if (viewDuration > 4 * 3600) { majorIntervalSec = 3600; timeFormat = "HH:mm"; }
        else if (viewDuration > 3600) { majorIntervalSec = 15 * 60; timeFormat = "HH:mm"; }
        else if (viewDuration > 10 * 60) { majorIntervalSec = 5 * 60; timeFormat = "HH:mm"; }
        else if (viewDuration > 2 * 60) { majorIntervalSec = 60; timeFormat = "HH:mm:ss"; }
        else if (viewDuration > 30) { majorIntervalSec = 10; timeFormat = "HH:mm:ss"; }
        else { majorIntervalSec = 5; timeFormat = "HH:mm:ss"; }

        // 对齐到整刻度
        var startSec = vStart.ToUnixTimeSeconds();
        var alignedSec = (startSec / majorIntervalSec) * majorIntervalSec;
        if (alignedSec < startSec) alignedSec += majorIntervalSec;

        var endSec = vEnd.ToUnixTimeSeconds();
        for (var sec = alignedSec; sec <= endSec; sec += majorIntervalSec)
        {
            var tickTime = DateTimeOffset.FromUnixTimeSeconds(sec).ToOffset(vStart.Offset);
            var x = (tickTime - vStart).TotalSeconds / viewDuration * width;
            if (x < 0 || x > width) continue;

            var label = tickTime.LocalDateTime.ToString(timeFormat, CultureInfo.CurrentCulture);
            var text = CachedText(label, 10, TimeBrush, pixelsPerDip);

            var textX = Math.Clamp(x - text.Width / 2, 2, Math.Max(2, width - text.Width - 2));
            drawing.DrawText(text, new Point(textX, 2));
            drawing.DrawLine(TickMajorPen, new Point(x, rulerHeight - 7), new Point(x, rulerHeight));

            // 次刻度
            var minorX = x + (majorIntervalSec / 2.0) / viewDuration * width;
            if (minorX < width)
            {
                drawing.DrawLine(TickMinorPen, new Point(minorX, rulerHeight - 4), new Point(minorX, rulerHeight));
            }
        }
    }

    private static FormattedText CachedText(string label, double size, Brush brush, double pixelsPerDip)
    {
        var key = $"{label}|{size}|{brush.GetHashCode()}|{pixelsPerDip:0.00}";
        lock (TextCacheGate)
        {
            if (TextCache.TryGetValue(key, out var cached)) return cached;
            if (TextCache.Count > 4096) TextCache.Clear();
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TimeTypeface, size, brush, pixelsPerDip);
            TextCache[key] = text;
            return text;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (End <= Start || ActualWidth <= 0) return;

        var vStart = ViewStart;
        var vEnd = ViewEnd;
        var currentDuration = (vEnd - vStart).TotalSeconds;
        if (currentDuration <= 0) return;

        var mouseX = Math.Clamp(e.GetPosition(this).X, 0, ActualWidth);
        var fraction = mouseX / ActualWidth;
        var anchor = PositionAt(vStart, vEnd, fraction);

        // 滚轮放大缩小（向上滚放大，向下滚缩小，最大显示24小时）
        var factor = e.Delta > 0 ? 0.75 : 1.3333;
        var maxDuration = Math.Min(86400.0, (End - Start).TotalSeconds);
        var newDuration = Math.Clamp(currentDuration * factor, 30, maxDuration);

        var newStart = anchor.AddSeconds(-fraction * newDuration);
        var newEnd = anchor.AddSeconds((1 - fraction) * newDuration);

        if (newStart < Start)
        {
            newStart = Start;
            newEnd = newStart.AddSeconds(newDuration);
        }
        if (newEnd > End)
        {
            newEnd = End;
            newStart = newEnd.AddSeconds(-newDuration);
            if (newStart < Start) newStart = Start;
        }

        _visibleStart = newStart;
        _visibleEnd = newEnd;
        InvalidateVisual();
        UpdateTooltip(anchor, false);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (End <= Start || ActualWidth <= 0) return;

        if (e.ClickCount == 2)
        {
            _visibleStart = null;
            _visibleEnd = null;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(this);
            _panStartRange = (ViewStart, ViewEnd);
            CaptureMouse();
            Cursor = Cursors.SizeWE;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            var pos = e.GetPosition(this);
            var vStart = ViewStart;
            var vEnd = ViewEnd;
            var clickTime = PositionAt(vStart, vEnd, pos.X / ActualWidth);

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _isSelectingRange = true;
                _selectionAnchor = clickTime;
                SelectedRangeStart = clickTime;
                SelectedRangeEnd = clickTime;
                CaptureMouse();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            _isDraggingPlayhead = true;
            _dragPosition = SnapToNearby(clickTime, ActualWidth, vStart, vEnd);
            CaptureMouse();
            InvalidateVisual();
            UpdateTooltip(_dragPosition, true);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (ActualWidth <= 0 || End <= Start) return;

        var mousePos = e.GetPosition(this);
        var vStart = ViewStart;
        var vEnd = ViewEnd;

        if (_isPanning)
        {
            var deltaX = mousePos.X - _panStartPoint.X;
            var duration = (_panStartRange.End - _panStartRange.Start).TotalSeconds;
            var deltaSec = -(deltaX / ActualWidth) * duration;

            var newStart = _panStartRange.Start.AddSeconds(deltaSec);
            var newEnd = _panStartRange.End.AddSeconds(deltaSec);

            if (newStart < Start)
            {
                var span = (newEnd - newStart).TotalSeconds;
                newStart = Start;
                newEnd = newStart.AddSeconds(span);
            }
            if (newEnd > End)
            {
                var span = (newEnd - newStart).TotalSeconds;
                newEnd = End;
                newStart = newEnd.AddSeconds(-span);
                if (newStart < Start) newStart = Start;
            }

            _visibleStart = newStart;
            _visibleEnd = newEnd;
            InvalidateVisual();
            return;
        }

        if (_isSelectingRange)
        {
            var curTime = PositionAt(vStart, vEnd, mousePos.X / ActualWidth);
            SelectedRangeStart = curTime < _selectionAnchor ? curTime : _selectionAnchor;
            SelectedRangeEnd = curTime > _selectionAnchor ? curTime : _selectionAnchor;
            InvalidateVisual();
            return;
        }

        if (_isDraggingPlayhead)
        {
            var rawTime = PositionAt(vStart, vEnd, mousePos.X / ActualWidth);
            _dragPosition = SnapToNearby(rawTime, ActualWidth, vStart, vEnd);
            UpdateTooltip(_dragPosition, true);
            InvalidateVisual();
            return;
        }

        var hoverTime = PositionAt(vStart, vEnd, mousePos.X / ActualWidth);
        UpdateTooltip(hoverTime, false);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isPanning && e.ChangedButton is MouseButton.Middle or MouseButton.Right)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            if (_isSelectingRange)
            {
                _isSelectingRange = false;
                ReleaseMouseCapture();
                if (SelectedRangeStart.HasValue && SelectedRangeEnd.HasValue && SelectedRangeEnd > SelectedRangeStart)
                {
                    RangeSelected?.Invoke(SelectedRangeStart.Value, SelectedRangeEnd.Value);
                }
                e.Handled = true;
                return;
            }

            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                ReleaseMouseCapture();
                SeekRequested?.Invoke(_dragPosition);
                InvalidateVisual();
                e.Handled = true;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right)) return;

        var stepSeconds = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 10;
        var target = (Position ?? Start).AddSeconds(e.Key == Key.Left ? -stepSeconds : stepSeconds);
        var clamped = target < Start ? Start : target > End ? End : target;
        SeekRequested?.Invoke(clamped);
        e.Handled = true;
    }

    private DateTimeOffset SnapToNearby(DateTimeOffset time, double width, DateTimeOffset vStart, DateTimeOffset vEnd)
    {
        var duration = (vEnd - vStart).TotalSeconds;
        if (duration <= 0 || width <= 0) return time;

        var snapThresholdSec = 8.0 * (duration / width); // 8 像素吸附距离
        var best = time;
        var minDiff = snapThresholdSec;

        var allSegs = Tracks is { Length: > 0 }
            ? Tracks.SelectMany(t => t.Segments ?? [])
            : (Segments ?? []);

        foreach (var seg in allSegs)
        {
            var dStart = Math.Abs((seg.Start - time).TotalSeconds);
            if (dStart < minDiff) { minDiff = dStart; best = seg.Start; }
            var dEnd = Math.Abs((seg.End - time).TotalSeconds);
            if (dEnd < minDiff) { minDiff = dEnd; best = seg.End; }
        }
        return best;
    }

    private void UpdateTooltip(DateTimeOffset time, bool isDragging)
    {
        var text = isDragging
            ? $"[定位时刻] {time.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
            : $"{time.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n(点击定位 · 滚轮缩放 · 右键平移 · Shift框选导出)";
        if (string.Equals(text, _lastTooltip, StringComparison.Ordinal)) return;
        _lastTooltip = text;
        ToolTip = text;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
