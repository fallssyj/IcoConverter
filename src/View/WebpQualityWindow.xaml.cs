using IcoConverter.Models;
using IcoConverter.ViewModels;

namespace IcoConverter;

public partial class WebpQualityWindow
{
    private readonly WebpQualityViewModel _viewModel;

    public WebpExportOptions Options => _viewModel.Options;

    public WebpQualityWindow(WebpExportOptions initial)
    {
        InitializeComponent();

        _viewModel = new WebpQualityViewModel(initial);
        DataContext = _viewModel;
    }
}
