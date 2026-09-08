using System.Windows;
using System.Windows.Controls;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
    private async void ChangePasswordClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        var current = CurrentPassword.Password; var next = NewPassword.Password; CurrentPassword.Clear(); NewPassword.Clear();
        await vm.ChangePasswordAsync(current, next);
    }
}
