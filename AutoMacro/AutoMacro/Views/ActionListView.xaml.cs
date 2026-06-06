using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace AutoMacro.Views;

public partial class ActionListView : UserControl
{
    public ActionListView()
    {
        InitializeComponent();
    }

    private void ActionDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }
}
