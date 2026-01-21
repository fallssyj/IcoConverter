using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace IcoConverter;

/// <summary>
/// ICO 转 PNG 弹窗。
/// </summary>
public partial class IcoToPngWindow
{
    private readonly IcoToPngViewModel _viewModel;

    public IcoToPngWindow()
    {
        InitializeComponent();

        _viewModel = App.ServiceProvider.GetRequiredService<IcoToPngViewModel>();
        _viewModel.RequestClose += () => Dispatcher.Invoke(Close);
        DataContext = _viewModel;
    }

    /// <summary>
    /// 初始化 ICO 路径并刷新数据。
    /// </summary>
    public void Initialize(string icoPath)
    {
        _viewModel.Initialize(icoPath);
    }
}
