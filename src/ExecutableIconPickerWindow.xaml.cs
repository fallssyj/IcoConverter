using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace IcoConverter;

/// <summary>
/// 可执行文件图标提取窗口。
/// </summary>
public partial class ExecutableIconPickerWindow
{
    private readonly ExecutableIconPickerViewModel _viewModel;

    public ExecutableIconPickerWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<ExecutableIconPickerViewModel>();
        _viewModel.RequestClose += () => Dispatcher.Invoke(Close);
        DataContext = _viewModel;
    }

    public void Initialize(string binaryPath)
    {
        _viewModel.Initialize(binaryPath);
    }
}
