using System.Windows;
using System.Windows.Controls;
using AutoMacro.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;

namespace AutoMacro.Views;

public partial class VariableEditorView : UserControl
{
    private VariableEditorViewModel? Vm => DataContext as VariableEditorViewModel;

    public VariableEditorView()
    {
        InitializeComponent();
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        Vm?.AddRow();
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var index = VarListBox.SelectedIndex;
        if (index < 0)
        {
            MessageBox.Show("请先选中要删除的行。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Vm.RemoveRow(index);
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var result = MessageBox.Show(
            "确定要清空所有变量数据吗？", "清空全部",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            Vm.ClearAll();
    }

    private void SaveCsv_Click(object sender, RoutedEventArgs e)
    {
        Vm?.SaveCsv();
        MessageBox.Show("变量表已保存。", "保存成功",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
