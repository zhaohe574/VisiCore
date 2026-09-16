namespace VideoPlatform.Desktop.ViewModels;

/// <summary>
/// 分屏格子的显示档位，决定该格应采用子码流还是主码流解码。
/// Hidden 表示该格不在当前档位内（应由工作区停止其会话）。
/// </summary>
public enum TileDisplayTier
{
    /// <summary>普通分屏格子：优先子码流，降低解码与显存带宽。</summary>
    Grid,
    /// <summary>单窗放大：使用主码流看清细节。</summary>
    Focused,
    /// <summary>全屏：使用主码流。</summary>
    Fullscreen,
    /// <summary>不在当前分屏档位内。</summary>
    Hidden
}
