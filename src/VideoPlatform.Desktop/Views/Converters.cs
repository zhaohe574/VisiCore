using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoPlatform.Desktop.Views;

public sealed class MatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Equals(value?.ToString(), parameter?.ToString());
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class MatchVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Equals(value?.ToString(), parameter?.ToString()) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class ModuleVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (parameter?.ToString() == "media" ? value?.ToString() is "live" or "playback" : Equals(value?.ToString(), parameter?.ToString())) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
/// <summary>引用相等判定：用于把视图模型实例（如当前分屏档位）绑定到单选按钮选中态。</summary>
public sealed class ReferenceEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ReferenceEquals(value, parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
/// <summary>
/// 多值引用相等判定。WPF 不允许在 ConverterParameter 上使用 Binding，
/// 因此“列表项 == 当前项”这类比较必须通过 MultiBinding 的两个参数完成。
/// </summary>
public sealed class MultiReferenceEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && ReferenceEquals(values[0], values[1]);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => (object)Binding.DoNothing).ToArray();
}
