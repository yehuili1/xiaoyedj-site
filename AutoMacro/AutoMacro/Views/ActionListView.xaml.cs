using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoMacro.Models;
using AutoMacro.ViewModels;
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
        e.Row.PreviewMouseRightButtonDown -= ActionDataGridRow_PreviewMouseRightButtonDown;
        e.Row.PreviewMouseRightButtonDown += ActionDataGridRow_PreviewMouseRightButtonDown;

        if (e.Row.ContextMenu is null)
        {
            var menu = new ContextMenu();
            var deleteItem = new MenuItem { Header = "删除这一步" };
            deleteItem.Click += DeleteStepMenuItem_Click;
            menu.Items.Add(deleteItem);
            e.Row.ContextMenu = menu;
        }
    }

    private void ActionDataGridRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGridRow row)
            return;

        row.IsSelected = true;
        row.Focus();
        ActionDataGrid.SelectedItem = row.Item;
    }

    private void DeleteStepMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActionDataGrid.SelectedItem is not InputEvent action)
            return;

        if (DataContext is ActionListViewModel viewModel)
            viewModel.DeleteAction(action);
    }

    private void PreviewImage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InputEvent action })
            return;

        if (string.IsNullOrWhiteSpace(action.ImagePath) || !File.Exists(action.ImagePath))
        {
            System.Windows.MessageBox.Show(
                "这张图片文件不存在，请重新添加图片。",
                "图片预览",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(action.ImagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        var image = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(12)
        };

        var previewWindow = new Window
        {
            Title = "图片预览",
            Width = 760,
            Height = 520,
            MinWidth = 360,
            MinHeight = 260,
            Content = image,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        previewWindow.ShowDialog();
    }
}
