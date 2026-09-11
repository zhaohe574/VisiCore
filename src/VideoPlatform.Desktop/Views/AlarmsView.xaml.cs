using System.Windows.Controls;
using System.Windows.Input;
using VideoPlatform.Desktop.ViewModels;

namespace VideoPlatform.Desktop.Views;
public partial class AlarmsView : UserControl
{
    public AlarmsView() => InitializeComponent();
    /// <summary>双击报警行联动实况预览（iVMS-4200 事件中心使用逻辑）。</summary>
    private void AlarmDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is AlarmsViewModel vm) vm.VideoCommand.Execute("live");
    }
}
