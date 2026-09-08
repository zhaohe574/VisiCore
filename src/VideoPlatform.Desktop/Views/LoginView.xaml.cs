using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView() => InitializeComponent();
    private async void LoginClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel { IsBusy: false } vm) return;
        var password = Password.Password; Password.Clear(); await vm.LoginAsync(password);
    }
    private void PasswordKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { LoginClicked(sender, e); e.Handled = true; } }
}
