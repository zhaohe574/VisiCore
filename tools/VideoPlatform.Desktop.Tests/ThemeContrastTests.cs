using System.Globalization;
using System.IO;
using Xunit;

namespace VideoPlatform.Desktop.Tests;

/// <summary>
/// 主题可读性门禁：解析 Themes/Light.xaml 与 Themes/Dark.xaml 的令牌，
/// 校验「文字色 / 面底色」组合满足 WCAG AA（正文 4.5:1，大字与图标 3:1）。
/// 任何新增或调色都要过这道门禁，避免再次出现浅色主题下看不清的问题。
/// </summary>
public sealed class ThemeContrastTests
{
    private static string ThemePath(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VideoPlatform.Desktop", "Themes", $"{name}.xaml"));

    private static Dictionary<string, string> LoadTokens(string name)
    {
        var path = ThemePath(name);
        Assert.True(File.Exists(path), $"找不到主题文件：{path}");
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var key = Between(line, "x:Key=\"", "\"");
            if (key is null || !line.Contains("<SolidColorBrush", StringComparison.Ordinal)) continue;
            var color = Between(line, "Color=\"", "\"");
            if (color is not null) tokens[key] = color;
        }
        return tokens;
    }

    private static string? Between(string line, string start, string end)
    {
        var from = line.IndexOf(start, StringComparison.Ordinal);
        if (from < 0) return null;
        from += start.Length;
        var to = line.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? null : line[from..to];
    }

    private static double RelativeLuminance(string hex)
    {
        var value = hex.TrimStart('#');
        Assert.Equal(6, value.Length);
        double Channel(int offset)
        {
            var raw = int.Parse(value.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return raw <= 0.03928 ? raw / 12.92 : Math.Pow((raw + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    private static double Contrast(string foreground, string background)
    {
        var a = RelativeLuminance(foreground);
        var b = RelativeLuminance(background);
        var (high, low) = a >= b ? (a, b) : (b, a);
        return Math.Round((high + 0.05) / (low + 0.05), 2);
    }

    /// <summary>正文与小字标签必须达到 4.5:1。</summary>
    public static IEnumerable<object[]> TextPairs() =>
    [
        // 浅色主题：文字 on 面底色
        ["Light", "TextBrush", "SurfaceBrush", 4.5],
        ["Light", "TextBrush", "PageBrush", 4.5],
        ["Light", "TextBrush", "SurfaceAltBrush", 4.5],
        ["Light", "TextStrongBrush", "SurfaceBrush", 4.5],
        ["Light", "MutedBrush", "SurfaceBrush", 4.5],
        ["Light", "MutedBrush", "SurfaceAltBrush", 4.5],
        ["Light", "MutedBrush", "PageBrush", 4.5],
        ["Light", "DisabledTextBrush", "SurfaceBrush", 3.0],
        ["Light", "OfflineTextBrush", "SurfaceBrush", 4.5],
        ["Light", "AccentTextBrush", "SurfaceBrush", 4.5],
        ["Light", "AccentTextBrush", "SurfaceAltBrush", 4.5],
        ["Light", "PrimaryTextBrush", "SurfaceBrush", 4.5],
        ["Light", "PrimaryTextBrush", "SurfaceAltBrush", 4.5],
        ["Light", "OnlineTextBrush", "SurfaceBrush", 4.5],
        ["Light", "OnlineTextBrush", "SurfaceAltBrush", 4.5],
        ["Light", "WarningTextBrush", "SurfaceBrush", 4.5],
        ["Light", "DangerTextBrush", "SurfaceBrush", 4.5],
        ["Light", "OnAccentBrush", "PrimaryBrush", 4.5],
        ["Light", "OnAccentBrush", "DangerBrush", 4.5],
        ["Light", "OnAccentEmphasisBrush", "AccentBrush", 4.5],
        ["Light", "OnAccentEmphasisBrush", "AccentHoverBrush", 4.5],
        // 深色主题：顶栏与状态栏文字
        ["Dark", "TextBrush", "SurfaceBrush", 4.5],
        ["Dark", "TextBrush", "PageBrush", 4.5],
        ["Dark", "MutedBrush", "SurfaceBrush", 4.5],
        ["Dark", "MutedBrush", "SurfaceAltBrush", 4.5],
        ["Dark", "OfflineTextBrush", "SurfaceBrush", 4.5],
        ["Dark", "AccentTextBrush", "SurfaceBrush", 4.5],
        ["Dark", "PrimaryTextBrush", "SurfaceBrush", 4.5],
        ["Dark", "OnlineTextBrush", "SurfaceBrush", 4.5],
        ["Dark", "WarningTextBrush", "SurfaceBrush", 4.5],
        ["Dark", "DangerTextBrush", "SurfaceBrush", 4.5],
        ["Dark", "OnAccentBrush", "PrimaryBrush", 4.5],
        ["Dark", "OnAccentEmphasisBrush", "AccentBrush", 4.5],
        ["Dark", "OnAccentEmphasisBrush", "AccentHoverBrush", 4.5]
    ];

    /// <summary>
    /// 两套主题共用的深色顶栏／状态栏文字。
    /// 刻意不校验 HeaderMutedBrush 在 HeaderPressBrush 上：按压态的背景只在鼠标按下的一瞬间出现，
    /// 并且所有深色顶栏控件在按压态都把前景换成 HeaderTextBrush（对比度 10+），因此该组合不会出现在成品界面上。
    /// </summary>
    public static IEnumerable<object[]> HeaderPairs() =>
    [
        ["HeaderTextBrush", "HeaderBrush", 4.5],
        ["HeaderMutedBrush", "HeaderBrush", 4.5],
        ["HeaderMutedBrush", "HeaderHoverBrush", 4.5],
        ["HeaderTextBrush", "HeaderHoverBrush", 4.5],
        ["HeaderTextBrush", "HeaderPressBrush", 4.5]
    ];

    [Theory]
    [MemberData(nameof(TextPairs))]
    public void ThemeTokensMeetWcagAa(string theme, string foreground, string background, double minimum)
    {
        var tokens = LoadTokens(theme);
        Assert.True(tokens.ContainsKey(foreground), $"{theme} 缺少令牌 {foreground}");
        Assert.True(tokens.ContainsKey(background), $"{theme} 缺少令牌 {background}");
        var ratio = Contrast(tokens[foreground], tokens[background]);
        Assert.True(ratio >= minimum, $"{theme} 主题 {foreground}({tokens[foreground]}) 在 {background}({tokens[background]}) 上对比度 {ratio} 低于 {minimum}");
    }

    [Theory]
    [MemberData(nameof(HeaderPairs))]
    public void HeaderTokensMeetWcagAa(string foreground, string background, double minimum)
    {
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var tokens = LoadTokens(theme);
            Assert.True(tokens.ContainsKey(foreground), $"{theme} 缺少令牌 {foreground}");
            Assert.True(tokens.ContainsKey(background), $"{theme} 缺少令牌 {background}");
            var ratio = Contrast(tokens[foreground], tokens[background]);
            Assert.True(ratio >= minimum, $"{theme} 主题 {foreground} 在 {background} 上对比度 {ratio} 低于 {minimum}");
        }
    }

    [Fact]
    public void BothThemesDefineTheSameTokenKeys()
    {
        var light = LoadTokens("Light").Keys.ToHashSet(StringComparer.Ordinal);
        var dark = LoadTokens("Dark").Keys.ToHashSet(StringComparer.Ordinal);
        var missingInDark = light.Except(dark, StringComparer.Ordinal).OrderBy(key => key).ToArray();
        var missingInLight = dark.Except(light, StringComparer.Ordinal).OrderBy(key => key).ToArray();
        Assert.True(missingInDark.Length == 0, $"Dark 主题缺少令牌：{string.Join("、", missingInDark)}");
        Assert.True(missingInLight.Length == 0, $"Light 主题缺少令牌：{string.Join("、", missingInLight)}");
    }

    [Fact]
    public void ViewsDoNotHardcodeColors()
    {
        // 视图与控件模板必须全部走主题令牌，硬编码颜色会在换肤时失真、也无法被对比度门禁覆盖。
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "VideoPlatform.Desktop"));
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            if (relative.StartsWith("Themes" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(file);
            var matches = System.Text.RegularExpressions.Regex.Matches(text, "#[0-9A-Fa-f]{6,8}");
            if (matches.Count > 0) offenders.Add($"{relative}（{matches.Count} 处）");
        }
        Assert.True(offenders.Count == 0, "以下视图仍存在硬编码颜色：" + string.Join("、", offenders));
    }
}
