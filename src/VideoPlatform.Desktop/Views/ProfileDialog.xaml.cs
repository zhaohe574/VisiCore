using System;
using System.Windows;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;

public partial class ProfileDialog : Window
{
    private readonly ShellViewModel _vm;

    public ProfileDialog(ShellViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => DisplayNameBox.Focus();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
    private void CancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.DisplayName))
        {
            StatusMessage.Text = "请输入姓名。";
            StatusMessage.Visibility = Visibility.Visible;
            DisplayNameBox.Focus();
            return;
        }

        try
        {
            await _vm.SaveProfileCommand.ExecuteAsync(null);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusMessage.Text = $"保存失败：{ex.Message}";
            StatusMessage.Visibility = Visibility.Visible;
        }
    }
}
