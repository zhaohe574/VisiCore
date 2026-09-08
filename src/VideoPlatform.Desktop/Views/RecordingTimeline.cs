using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VideoPlatform.Desktop.Models;

namespace VideoPlatform.Desktop.Views;

public sealed class RecordingTimeline : FrameworkElement
{
    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(nameof(Start), typeof(DateTimeOffset), typeof(RecordingTimeline), new FrameworkPropertyMetadata(DateTimeOffset.Now.AddHours(-1), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(nameof(End), typeof(DateTimeOffset), typeof(RecordingTimeline), new FrameworkPropertyMetadata(DateTimeOffset.Now, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(nameof(Position), typeof(DateTimeOffset?), typeof(RecordingTimeline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(nameof(Segments), typeof(RecordingSegment[]), typeof(RecordingTimeline), new FrameworkPropertyMetadata(Array.Empty<RecordingSegment>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public DateTimeOffset Start { get => (DateTimeOffset)GetValue(StartProperty); set => SetValue(StartProperty, value); }
    public DateTimeOffset End { get => (DateTimeOffset)GetValue(EndProperty); set => SetValue(EndProperty, value); }
    public DateTimeOffset? Position { get => (DateTimeOffset?)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public RecordingSegment[] Segments { get => (RecordingSegment[])GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public event Action<DateTimeOffset>? SeekRequested;
    public RecordingTimeline() { Height = 62; Cursor = Cursors.Hand; Focusable = true; }
    public static DateTimeOffset PositionAt(DateTimeOffset start, DateTimeOffset end, double fraction) => start.AddTicks((long)((end - start).Ticks * Math.Clamp(fraction, 0, 1)));
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var width = ActualWidth;
        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(31, 33, 36)), null, new Rect(0, 0, width, ActualHeight));
        if (End <= Start || width < 2) return;
        var duration = (End - Start).TotalSeconds;
        double X(DateTimeOffset time) => Math.Clamp((time - Start).TotalSeconds / duration, 0, 1) * width;
        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(65, 68, 73)), null, new Rect(0, 29, width, 19));
        var segmentBrush = new SolidColorBrush(Color.FromRgb(65, 157, 132));
        foreach (var segment in Segments ?? [])
        {
            if (segment.End <= Start || segment.Start >= End) continue;
            drawing.DrawRectangle(segmentBrush, null, new Rect(X(segment.Start), 30, Math.Max(1, X(segment.End) - X(segment.Start)), 17));
        }
        var ticks = Math.Max(2, (int)(width / 115));
        for (var i = 0; i <= ticks; ++i)
        {
            var fraction = (double)i / ticks;
            var time = PositionAt(Start, End, fraction);
            var text = new FormattedText(time.LocalDateTime.ToString("HH:mm:ss"), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 11, Brushes.LightGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var x = fraction * width;
            drawing.DrawText(text, new Point(Math.Clamp(x - text.Width / 2, 0, Math.Max(0, width - text.Width)), 5));
            drawing.DrawLine(new Pen(Brushes.Gray, 1), new Point(x, 23), new Point(x, 28));
        }
        if (Position is { } position && position >= Start && position <= End) drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(241, 180, 108)), 2), new Point(X(position), 24), new Point(X(position), 55));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); Focus();
        if (ActualWidth > 0 && End > Start) SeekRequested?.Invoke(PositionAt(Start, End, e.GetPosition(this).X / ActualWidth));
        e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (ActualWidth > 0) ToolTip = PositionAt(Start, End, e.GetPosition(this).X / ActualWidth).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Left or Key.Right)) return;
        var target = (Position ?? Start).AddSeconds(e.Key == Key.Left ? -10 : 10);
        SeekRequested?.Invoke(target < Start ? Start : target > End ? End : target); e.Handled = true;
    }
}
