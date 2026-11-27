using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;

namespace IcoConverter;

public partial class ExecutableIconPickerWindow
{
    private readonly ExecutableIconPickerViewModel _viewModel;

    public ExecutableIconPickerWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<ExecutableIconPickerViewModel>();
        _viewModel.RequestClose += () => Dispatcher.Invoke(Close);
        DataContext = _viewModel;

        // 首次渲染后同步棋盘格，以采用窗口初始化后的主题。
        ContentRendered += (_, _) => _viewModel.RefreshTheme();
    }

    public void Initialize(string binaryPath)
    {
        _viewModel.Initialize(binaryPath);
    }

    private void OnIconTileMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExecutableIconOption option })
        {
            _viewModel.HoverOption(option);
        }
    }

    private void OnIconListMouseLeave(object sender, MouseEventArgs e)
    {
        _viewModel.HoverOption(null);
    }

    private void OnIconTileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ExecutableIconOption option })
        {
            _viewModel.AnchorOption(option);
        }
    }
}
