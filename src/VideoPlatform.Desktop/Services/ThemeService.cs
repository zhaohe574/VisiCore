using System.Windows;

namespace VideoPlatform.Desktop.Services;

/// <summary>在运行时替换 App 资源中的主题令牌字典（位置 0），控件模板通过 DynamicResource 自动跟随。</summary>
public static class ThemeService
{
    public const string Light = "light";
    public const string Dark = "dark";

    public static string Normalize(string? theme) => string.Equals(theme, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;

    /// <summary>应用主题；资源尚未加载或字典缺失时返回 false，不抛出异常。</summary>
    public static bool Apply(string? theme)
    {
        var application = Application.Current;
        if (application is null) return false;
        var normalized = Normalize(theme);
        var uri = new Uri($"/VideoPlatform.Desktop;component/Themes/{(normalized == Dark ? "Dark" : "Light")}.xaml", UriKind.Relative);
        try
        {
            if (application.Resources.MergedDictionaries.Count == 0) return false;
            application.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = uri };
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
