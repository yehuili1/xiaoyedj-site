using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoMacro.Models;
using AutoMacro.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace AutoMacro.Views;

public partial class ActionListView : UserControl
{
    private System.Windows.Point? _dragStartPoint;
    private InputEvent? _draggedAction;
    private bool _isRowDragging;
    private int _pendingDropIndex = -1;

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
            var editItem = new MenuItem { Header = "\u4fee\u6539\u8fd9\u4e00\u6b65" };
            editItem.Click += EditStepMenuItem_Click;
            menu.Items.Add(editItem);
            menu.Items.Add(new Separator());
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

    private void ActionDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EndRowDrag(releaseCapture: false);

        var source = e.OriginalSource as DependencyObject;
        if (FindVisualParent<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
            FindVisualParent<System.Windows.Controls.Primitives.ScrollBar>(source) is not null ||
            FindVisualParent<DataGridColumnHeader>(source) is not null)
        {
            _dragStartPoint = null;
            _draggedAction = null;
            return;
        }

        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is not InputEvent action)
        {
            _dragStartPoint = null;
            _draggedAction = null;
            return;
        }

        _dragStartPoint = e.GetPosition(ActionDataGrid);
        _draggedAction = action;
        row.IsSelected = true;
        row.Focus();
        ActionDataGrid.SelectedItem = action;
    }

    private void ActionDataGrid_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStartPoint is null || _draggedAction is null)
            return;

        var currentPoint = e.GetPosition(ActionDataGrid);
        if (!_isRowDragging)
        {
            if (Math.Abs(currentPoint.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _isRowDragging = true;
            ActionDataGrid.CaptureMouse();
        }

        UpdateDropIndicator(currentPoint);
        e.Handled = true;
    }

    private void ActionDataGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isRowDragging)
        {
            CommitRowDrag();
            e.Handled = true;
        }

        EndRowDrag(releaseCapture: true);
    }

    private void ActionDataGrid_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        EndRowDrag(releaseCapture: false);
    }

    private void DeleteStepMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActionDataGrid.SelectedItem is not InputEvent action)
            return;

        if (DataContext is ActionListViewModel viewModel)
            viewModel.DeleteAction(action);
    }

    private void EditStepMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (ActionDataGrid.SelectedItem is not InputEvent action)
            return;

        if (DataContext is ActionListViewModel viewModel)
            viewModel.EditAction(action);
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

    private void UpdateDropIndicator(System.Windows.Point point)
    {
        if (DataContext is not ActionListViewModel viewModel || _draggedAction is null)
            return;

        var insertionIndex = ResolveInsertionIndex(point, viewModel);
        if (insertionIndex < 0)
        {
            HideDropIndicator();
            return;
        }

        _pendingDropIndex = insertionIndex;
        ShowDropIndicator(insertionIndex, viewModel.Actions.Count);
    }

    private void CommitRowDrag()
    {
        if (DataContext is not ActionListViewModel viewModel ||
            _draggedAction is null ||
            _pendingDropIndex < 0)
        {
            return;
        }

        var oldIndex = viewModel.Actions.IndexOf(_draggedAction);
        if (oldIndex < 0)
            return;

        var targetIndex = _pendingDropIndex > oldIndex
            ? _pendingDropIndex - 1
            : _pendingDropIndex;

        targetIndex = Math.Clamp(targetIndex, 0, Math.Max(0, viewModel.Actions.Count - 1));
        if (targetIndex != oldIndex)
            viewModel.MoveAction(_draggedAction, targetIndex);

        RefreshRowHeaders();
    }

    private int ResolveInsertionIndex(System.Windows.Point point, ActionListViewModel viewModel)
    {
        var targetRow = GetRowAtPoint(point);
        if (targetRow?.Item is not InputEvent targetAction)
        {
            return point.Y < 0
                ? 0
                : viewModel.Actions.Count;
        }

        var targetIndex = viewModel.Actions.IndexOf(targetAction);
        if (targetIndex < 0)
            return -1;

        var rowTop = targetRow.TransformToAncestor(ActionDataGrid).Transform(new System.Windows.Point(0, 0)).Y;
        if (point.Y > rowTop + targetRow.ActualHeight / 2)
            targetIndex++;

        return Math.Clamp(targetIndex, 0, viewModel.Actions.Count);
    }

    private DataGridRow? GetRowAtPoint(System.Windows.Point point)
    {
        var hit = VisualTreeHelper.HitTest(ActionDataGrid, point)?.VisualHit;
        return FindVisualParent<DataGridRow>(hit);
    }

    private void RefreshRowHeaders()
    {
        for (var i = 0; i < ActionDataGrid.Items.Count; i++)
        {
            if (ActionDataGrid.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow row)
                row.Header = (i + 1).ToString();
        }
    }

    private void ShowDropIndicator(int insertionIndex, int itemCount)
    {
        var y = GetDropIndicatorY(insertionIndex, itemCount);
        if (y is null)
        {
            HideDropIndicator();
            return;
        }

        DropIndicator.Margin = new Thickness(0, Math.Max(0, y.Value - 1), 0, 0);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private double? GetDropIndicatorY(int insertionIndex, int itemCount)
    {
        if (itemCount <= 0)
            return null;

        if (insertionIndex <= 0)
            return GetRowEdgeY(0, bottom: false);

        if (insertionIndex >= itemCount)
            return GetRowEdgeY(itemCount - 1, bottom: true);

        return GetRowEdgeY(insertionIndex, bottom: false);
    }

    private double? GetRowEdgeY(int rowIndex, bool bottom)
    {
        if (ActionDataGrid.ItemContainerGenerator.ContainerFromIndex(rowIndex) is not DataGridRow row)
            return null;

        var y = row.TransformToAncestor(ActionGridHost).Transform(new System.Windows.Point(0, 0)).Y;
        return bottom ? y + row.ActualHeight : y;
    }

    private void HideDropIndicator()
    {
        DropIndicator.Visibility = Visibility.Collapsed;
        _pendingDropIndex = -1;
    }

    private void EndRowDrag(bool releaseCapture)
    {
        _dragStartPoint = null;
        _draggedAction = null;
        _isRowDragging = false;
        HideDropIndicator();

        if (releaseCapture && ActionDataGrid.IsMouseCaptured)
            ActionDataGrid.ReleaseMouseCapture();

        RefreshRowHeaders();
    }

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed)
                return typed;

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
