using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;
using IcoConverter.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IcoConverter.ViewModels;

public partial class IcoResolutionPickerViewModel : ObservableObject
{
    private bool? _dialogResult;

    public ObservableCollection<IcoResolution> ResolutionItems { get; }

    public string SourceImageSizeText { get; }

    public IconExportFormat Format { get; }

    public string WindowTitle => Format switch
    {
        IconExportFormat.Icns => "导出 ICNS",
        IconExportFormat.Png => "导出 PNG 图标集",
        _ => "导出 ICO"
    };

    public string IntroductionText => Format == IconExportFormat.Png
        ? "勾选需要的尺寸，每个尺寸输出一张 PNG 到所选文件夹。"
        : "勾选后仅导出选中的尺寸。";

    public bool IsHintOpen => Format == IconExportFormat.Ico
        && ResolutionItems.Any(r => r.IsSelected && r.IsBeyondIcoLimit);

    // 帧编码仅对 ICO 有效，ICNS 与 PNG 集无此选项
    public bool IsFrameModeVisible => Format == IconExportFormat.Ico;

    public ObservableCollection<IcoPngFrameMode> IcoPngFrameModeOptions { get; }
        = new()
        {
            IcoPngFrameMode.Auto,
            IcoPngFrameMode.AlwaysPng,
            IcoPngFrameMode.NeverPng
        };

    private IcoPngFrameMode _frameMode = IcoPngFrameMode.Auto;

    public IcoPngFrameMode FrameMode
    {
        get => _frameMode;
        set => SetProperty(ref _frameMode, value);
    }

    public string HintTitle => "超出标准 ICO 尺寸";

    public string HintMessage => "标准 ICO 尺寸最大为 256×256。仍可导出所选尺寸，但部分软件可能将较大的图像识别为 256×256，或跳过这些图像。";

    public IReadOnlyList<System.Drawing.Size> SelectedResolutions => ResolutionItems
        .Where(r => r.IsSelected)
        .Select(r => new System.Drawing.Size(r.Width, r.Height))
        .Distinct()
        .OrderBy(size => size.Width)
        .ToList();

    private IcoResolution? _selectedResolution;

    /// <summary>右侧放大的当前档位，随列表选中项变化。</summary>
    public IcoResolution? SelectedResolution
    {
        get => _selectedResolution;
        set
        {
            if (SetProperty(ref _selectedResolution, value))
            {
                OnPropertyChanged(nameof(SelectedPreviewImage));
                OnPropertyChanged(nameof(SelectedPreviewLabel));
            }
        }
    }

    public BitmapSource? SelectedPreviewImage => _selectedResolution?.Preview;

    public string SelectedPreviewLabel => _selectedResolution == null
        ? "在左边选一档看效果"
        : $"{_selectedResolution.Width} × {_selectedResolution.Height} px";

    /// <summary>
    /// 将传入图像等比缩放并居中放入目标尺寸，剩余区域保持透明。
    /// </summary>
    private static BitmapSource? RenderPreview(BitmapSource? source, int width, int height)
    {
        if (source == null || width <= 0 || height <= 0)
        {
            return null;
        }

        try
        {
            var scale = Math.Min((double)width / source.PixelWidth, (double)height / source.PixelHeight);
            var drawWidth = source.PixelWidth * scale;
            var drawHeight = source.PixelHeight * scale;

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var context = visual.RenderOpen())
            {
                context.DrawImage(source, new Rect(
                    (width - drawWidth) / 2,
                    (height - drawHeight) / 2,
                    drawWidth,
                    drawHeight));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            // 缩放失败时视为无缩略图，不阻断尺寸选择流程
            return null;
        }
    }

    public IRelayCommand SelectAllCommand { get; }

    public IRelayCommand ClearAllCommand { get; }

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public IcoResolutionPickerViewModel(
        IEnumerable<IcoResolution> resolutions,
        string sourceImageSizeText,
        IconExportFormat format = IconExportFormat.Ico,
        IcoPngFrameMode frameMode = IcoPngFrameMode.Auto,
        BitmapSource? sourceImage = null)
    {
        Format = format;
        _frameMode = frameMode;

        SourceImageSizeText = string.IsNullOrWhiteSpace(sourceImageSizeText)
            ? "当前图像尺寸: 未知"
            : $"当前图像尺寸: {sourceImageSizeText}";

        // ICNS 档位固定，不支持的尺寸不予列出，避免选择后被跳过
        var candidates = Format == IconExportFormat.Icns
            ? resolutions.Where(r => r.IsSupportedByIcns)
            : resolutions;

        var items = candidates
            .OrderBy(r => r.Width)
            .ThenBy(r => r.Height)
            .Select(r => new IcoResolution(r.Width, r.Height, r.IsSelected))
            .ToList();

        if (Format == IconExportFormat.Icns && items.Count == 0)
        {
            items = IcoResolution.IcnsSizes
                .Select(size => new IcoResolution(size, size, true))
                .ToList();
        }

        ResolutionItems = new ObservableCollection<IcoResolution>(items);

        foreach (var item in ResolutionItems)
        {
            item.Preview = RenderPreview(sourceImage, item.Width, item.Height);
            item.ApplyFormatHint(Format);
            item.PropertyChanged += OnResolutionPropertyChanged;
        }

        // 打开窗口时预览最小尺寸，保留已有的导出勾选状态。
        SelectedResolution = ResolutionItems.FirstOrDefault();

        SelectAllCommand = new RelayCommand(SelectAll);
        ClearAllCommand = new RelayCommand(ClearAll);
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void OnResolutionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IcoResolution.IsSelected))
        {
            OnPropertyChanged(nameof(IsHintOpen));
        }
    }

    private void SelectAll()
    {
        foreach (var resolution in ResolutionItems)
        {
            resolution.IsSelected = true;
        }
    }

    private void ClearAll()
    {
        foreach (var resolution in ResolutionItems)
        {
            resolution.IsSelected = false;
        }
    }

    private void Confirm()
    {
        if (ResolutionItems.All(r => !r.IsSelected))
        {
            AppMessageBox.Show("请至少选择一个尺寸。", WindowTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel()
    {
        DialogResult = false;
    }
}
