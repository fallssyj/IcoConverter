using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace IcoConverter;

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

    public void Initialize(string iconPath)
    {
        _viewModel.Initialize(iconPath);
    }
}
