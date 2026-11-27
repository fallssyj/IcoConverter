using IcoConverter.Models;
using IcoConverter.ViewModels;
using System.Windows.Media.Imaging;

namespace IcoConverter;

public partial class IcoResolutionPickerWindow
{
    private readonly IcoResolutionPickerViewModel _viewModel;

    public IReadOnlyList<System.Drawing.Size> SelectedResolutions => _viewModel.SelectedResolutions;

    public IReadOnlyList<IcoResolution> ResolutionItems => _viewModel.ResolutionItems;

    public IcoPngFrameMode FrameMode => _viewModel.FrameMode;

    public IcoResolutionPickerWindow(
        IEnumerable<IcoResolution> resolutions,
        string sourceImageSizeText,
        IconExportFormat format = IconExportFormat.Ico,
        IcoPngFrameMode frameMode = IcoPngFrameMode.Auto,
        BitmapSource? sourceImage = null)
    {
        InitializeComponent();

        _viewModel = new IcoResolutionPickerViewModel(resolutions, sourceImageSizeText, format, frameMode, sourceImage);
        DataContext = _viewModel;
    }
}
