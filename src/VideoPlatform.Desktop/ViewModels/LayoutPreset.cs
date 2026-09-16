namespace VideoPlatform.Desktop.ViewModels;

/// <summary>分屏格位：在档位网格中的行列位置与跨度。0 跨度表示该格位不在当前档位内。</summary>
public readonly record struct LayoutSlot(int Row, int Column, int RowSpan, int ColumnSpan)
{
    public static readonly LayoutSlot None = new(0, 0, 0, 0);
}

/// <summary>
/// iVMS-4200 风格分屏档位。除等分的 1/4/9/16/25 外，提供“1 大屏 + n 小屏”的聚焦档位：
/// 1+7（4×4 网格，大屏 3×3 + 7 小屏）与 1+9（5×5 网格，大屏 4×4 + 9 小屏），小屏铺在右侧列与底部行，格位完全填满且无黑屏空位。
/// </summary>
public sealed record LayoutPreset(string Key, int Count, int Columns, int Rows, int FocusSpan)
{
    public string Label => Key;
    public bool HasFocusCell => FocusSpan > 0;

    public static LayoutPreset? Find(IEnumerable<LayoutPreset> presets, string? key)
    {
        if (key is null) return null;
        var trimmed = key.Trim();
        var preset = presets.FirstOrDefault(p => string.Equals(p.Key, trimmed, StringComparison.OrdinalIgnoreCase));
        if (preset is not null) return preset;
        if (int.TryParse(trimmed, out var count))
            return presets.FirstOrDefault(p => p.Count == count);
        return null;
    }

    /// <summary>返回该格在网格中的位置与跨度；超出档位格数的格位返回 None（由视图折叠该格）。</summary>
    public LayoutSlot SlotFor(int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= Count) return LayoutSlot.None;
        if (!HasFocusCell)
        {
            var columns = Math.Max(Columns, 1);
            return new LayoutSlot(tileIndex / columns, tileIndex % columns, 1, 1);
        }
        if (tileIndex == 0) return new LayoutSlot(0, 0, FocusSpan, FocusSpan);
        // 大屏固定左上角，其余格子按格子顺序寻找第一个未被占用的单元，
        // 1+7（4×4，大屏 3×3，7 个小屏共 16 格）与 1+9（5×5，大屏 4×4，9 个小屏共 25 格）都能无重叠且无空位地放满网格。
        var occupied = new bool[Rows, Columns];
        for (var row = 0; row < FocusSpan && row < Rows; ++row)
            for (var column = 0; column < FocusSpan && column < Columns; ++column)
                occupied[row, column] = true;
        var remaining = tileIndex - 1;
        for (var row = 0; row < Rows; ++row)
            for (var column = 0; column < Columns; ++column)
            {
                if (occupied[row, column]) continue;
                if (remaining-- == 0) return new LayoutSlot(row, column, 1, 1);
            }
        return LayoutSlot.None;
    }
}
