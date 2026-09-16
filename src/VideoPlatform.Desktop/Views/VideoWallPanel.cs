using System.Windows;
using System.Windows.Controls;

namespace VideoPlatform.Desktop.Views;

/// <summary>
/// 视频墙布局面板：按每格 VideoTileViewModel 的行列位置与跨度生成网格。
/// 相较 UniformGrid 支持 iVMS-4200 的“1 大屏 + n 小屏”聚焦档位，
/// 并在档位切换时复用已经存在的 VideoTileView 实例（不重建原生播放器窗口）。
/// </summary>
public sealed class VideoWallPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var (rows, columns) = MeasureGrid();
        if (rows == 0 || columns == 0)
        {
            foreach (UIElement child in Children) child.Measure(new Size(0, 0));
            return new Size(0, 0);
        }
        var cellWidth = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width / columns;
        var cellHeight = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height / rows;
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Measure(new Size(0, 0));
                continue;
            }
            var (rowSpan, columnSpan) = SpanOf(child);
            child.Measure(new Size(cellWidth * columnSpan, cellHeight * rowSpan));
        }
        // 面板自身严格填满可用空间：视频墙必须拿到有限尺寸，不能由视频内容反向顶大。
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (rows, columns) = MeasureGrid();
        if (rows == 0 || columns == 0) return finalSize;
        var cellWidth = finalSize.Width / columns;
        var cellHeight = finalSize.Height / rows;
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var (rowSpan, columnSpan) = SpanOf(child);
            if (rowSpan <= 0 || columnSpan <= 0)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var (row, column) = PositionOf(child);
            var x1 = Math.Round(column * cellWidth);
            var x2 = Math.Round((column + columnSpan) * cellWidth);
            var y1 = Math.Round(row * cellHeight);
            var y2 = Math.Round((row + rowSpan) * cellHeight);
            child.Arrange(new Rect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1)));
        }
        return finalSize;
    }

    private (int Rows, int Columns) MeasureGrid()
    {
        var rows = 0;
        var columns = 0;
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var (rowSpan, columnSpan) = SpanOf(child);
            if (rowSpan <= 0 || columnSpan <= 0) continue;
            var (row, column) = PositionOf(child);
            rows = Math.Max(rows, row + rowSpan);
            columns = Math.Max(columns, column + columnSpan);
        }
        return (rows, columns);
    }

    private static (int RowSpan, int ColumnSpan) SpanOf(UIElement child)
    {
        if (child is not FrameworkElement element) return (1, 1);
        var rowSpan = Grid.GetRowSpan(element);
        var columnSpan = Grid.GetColumnSpan(element);
        return (Math.Max(rowSpan, 0), Math.Max(columnSpan, 0));
    }

    private static (int Row, int Column) PositionOf(UIElement child)
    {
        if (child is not FrameworkElement element) return (0, 0);
        return (Math.Max(Grid.GetRow(element), 0), Math.Max(Grid.GetColumn(element), 0));
    }
}
