using System;
using System.Windows;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class ChangePasswordDialog : Window
{
    private readonly ShellViewModel _vm;

    public ChangePasswordDialog(ShellViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => CurrentPasswordBox.Focus();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void SubmitClicked(object sender, RoutedEventArgs e)
    {
        var current = CurrentPasswordBox.Password;
        var next = NewPasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrEmpty(current))
        {
            ShowError("请输入当前密码。");
            CurrentPasswordBox.Focus();
            return;
        }

        if (string.IsNullOrEmpty(next))
        {
            ShowError("请输入新密码。");
            NewPasswordBox.Focus();
            return;
        }

        if (next != confirm)
        {
            ShowError("两次输入的新密码不一致，请重新核对。");
            ConfirmPasswordBox.Focus();
            return;
        }

        try
        {
            await _vm.ChangePasswordAsync(current, next);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ShowError($"修改失败：{ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage.Text = message;
        ErrorMessage.Visibility = Visibility.Visible;
    }
}
