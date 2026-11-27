using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;

namespace IcoConverter.ViewModels;

public class WebpQualityViewModel : ObservableObject
{
    private bool? _dialogResult;
    private bool _isLossy;
    private int _quality;

    public WebpQualityViewModel(WebpExportOptions initial)
    {
        _isLossy = !initial.IsLossless;
        _quality = Math.Clamp(initial.Quality, WebpExportOptions.MinQuality, WebpExportOptions.MaxQuality);

        ConfirmCommand = new RelayCommand(() => DialogResult = true);
        CancelCommand = new RelayCommand(() => DialogResult = false);
    }

    public string HeaderText => "WebP 导出设置";

    public string IntroductionText => "有损压缩体积更小、质量可调；无损逐像素保留，文件通常比 PNG 还大。";

    public bool IsLossy
    {
        get => _isLossy;
        set
        {
            if (SetProperty(ref _isLossy, value))
            {
                OnPropertyChanged(nameof(IsLossless));
                OnPropertyChanged(nameof(QualityHint));
                OnPropertyChanged(nameof(Options));
            }
        }
    }

    // 两个单选按钮分别绑定布尔值，彼此互斥
    public bool IsLossless
    {
        get => !_isLossy;
        set => IsLossy = !value;
    }

    public int Quality
    {
        get => _quality;
        set
        {
            var safeValue = Math.Clamp(value, WebpExportOptions.MinQuality, WebpExportOptions.MaxQuality);
            if (SetProperty(ref _quality, safeValue))
            {
                OnPropertyChanged(nameof(Options));
            }
        }
    }

    public string QualityHint => _isLossy
        ? "数值越高越接近原图，体积也越大。图标一般 90 以上就够。"
        : "无损模式按最高压缩比写入，质量数值不参与计算。";

    public WebpExportOptions Options => new(!_isLossy, _isLossy ? _quality : WebpExportOptions.MaxQuality);

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }
}
