using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;

namespace IcoConverter;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // 设置数据上下文
        _viewModel = App.ServiceProvider.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        // 设置拖放事件
        this.PreviewDragOver += MainWindow_PreviewDragOver;
        this.PreviewDrop += MainWindow_PreviewDrop;
        this.PreviewDragLeave += MainWindow_PreviewDragLeave;
    }
    /// <summary>
    /// 处理窗口鼠标左键按下事件
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">包含事件数据的<see cref="MouseButtonEventArgs"/></param>
    /// <remarks>
    /// 将鼠标事件传递给ViewModel的<see cref="MainViewModel.MouseLeftButtonDownCommand"/>命令，
    /// 用于实现窗口拖动功能。
    /// </remarks>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.MouseLeftButtonDownCommand.Execute(e);
        }
    }
    private void MainWindow_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
            _viewModel.IsDropHintVisible = true;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
            _viewModel.IsDropHintVisible = false;
        }
    }

    private void MainWindow_PreviewDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            _viewModel.HandleFileDrop(files);
            e.Handled = true;
        }

        _viewModel.IsDropHintVisible = false;
    }

    private void MainWindow_PreviewDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        // 用户拖拽离开窗口视为取消操作，隐藏拖放提示
        _viewModel.IsDropHintVisible = false;
    }

    private void SelectAllResolutions_Click(object sender, RoutedEventArgs e)
    {
        foreach (var resolution in _viewModel.AvailableResolutions)
        {
            resolution.IsSelected = true;
        }
    }

    private void ClearAllResolutions_Click(object sender, RoutedEventArgs e)
    {
        foreach (var resolution in _viewModel.AvailableResolutions)
        {
            resolution.IsSelected = false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        System.Windows.Application.Current.Shutdown();
    }

    private void LogTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            // 滚动到文本末尾
            textBox.ScrollToEnd();

            // 确保光标在末尾
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    private void CornerRadiusSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Delta > 0)
        {
            if (viewModel.IncreaseCornerRadiusCommand.CanExecute(null))
            {
                viewModel.IncreaseCornerRadiusCommand.Execute(null);
            }
        }
        else if (e.Delta < 0)
        {
            if (viewModel.DecreaseCornerRadiusCommand.CanExecute(null))
            {
                viewModel.DecreaseCornerRadiusCommand.Execute(null);
            }
        }

        e.Handled = true;
    }

    private void PolygonRotationSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (e.Delta > 0)
        {
            if (viewModel.IncreasePolygonRotationCommand.CanExecute(null))
            {
                viewModel.IncreasePolygonRotationCommand.Execute(null);
            }
        }
        else if (e.Delta < 0)
        {
            if (viewModel.DecreasePolygonRotationCommand.CanExecute(null))
            {
                viewModel.DecreasePolygonRotationCommand.Execute(null);
            }
        }

        e.Handled = true;
    }
}
