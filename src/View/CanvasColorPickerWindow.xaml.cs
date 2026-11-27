using IcoConverter.ViewModels;

namespace IcoConverter;

public partial class CanvasColorPickerWindow
{
    private readonly CanvasColorPickerViewModel _viewModel;

    public string SelectedColorHex => _viewModel.SelectedColorHex;

    public CanvasColorPickerWindow(string? initialHex)
    {
        InitializeComponent();

        _viewModel = new CanvasColorPickerViewModel(initialHex);
        DataContext = _viewModel;
    }
}
