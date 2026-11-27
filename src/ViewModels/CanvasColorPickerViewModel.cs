using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace IcoConverter.ViewModels;

public class CanvasColorPickerViewModel : ObservableObject
{
    private bool? _dialogResult;
    private int _alpha = 255;
    private int _red;
    private int _green;
    private int _blue;

    public CanvasColorPickerViewModel(string? initialHex)
    {
        ApplyColor(ParseHex(initialHex));

        Presets = new ObservableCollection<ColorPresetOption>(CreatePresetOptions());
        ApplyPresetCommand = new RelayCommand<string>(ApplyPreset);
        ConfirmCommand = new RelayCommand(() => DialogResult = true);
        CancelCommand = new RelayCommand(() => DialogResult = false);
    }

    public ObservableCollection<ColorPresetOption> Presets { get; }

    public IRelayCommand<string> ApplyPresetCommand { get; }

    public IRelayCommand ConfirmCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public int Alpha
    {
        get => _alpha;
        set
        {
            var safeValue = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _alpha, safeValue))
            {
                RaiseColorChanged();
            }
        }
    }

    public int Red
    {
        get => _red;
        set
        {
            var safeValue = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _red, safeValue))
            {
                RaiseColorChanged();
            }
        }
    }

    public int Green
    {
        get => _green;
        set
        {
            var safeValue = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _green, safeValue))
            {
                RaiseColorChanged();
            }
        }
    }

    public int Blue
    {
        get => _blue;
        set
        {
            var safeValue = Math.Clamp(value, 0, 255);
            if (SetProperty(ref _blue, safeValue))
            {
                RaiseColorChanged();
            }
        }
    }

    public string SelectedColorHex => $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}";

    public MediaBrush PreviewBrush => new SolidColorBrush(MediaColor.FromArgb((byte)Alpha, (byte)Red, (byte)Green, (byte)Blue));

    private void ApplyPreset(string? hex)
    {
        ApplyColor(ParseHex(hex));
    }

    private void ApplyColor(MediaColor color)
    {
        _alpha = color.A;
        _red = color.R;
        _green = color.G;
        _blue = color.B;

        OnPropertyChanged(nameof(Alpha));
        OnPropertyChanged(nameof(Red));
        OnPropertyChanged(nameof(Green));
        OnPropertyChanged(nameof(Blue));
        RaiseColorChanged();
    }

    private void RaiseColorChanged()
    {
        OnPropertyChanged(nameof(SelectedColorHex));
        OnPropertyChanged(nameof(PreviewBrush));
    }

    private static IEnumerable<ColorPresetOption> CreatePresetOptions()
    {
        var presets = new[]
        {
            ("透明", "#00FFFFFF"),
            ("白色", "#FFFFFFFF"),
            ("黑色", "#FF000000"),
            ("浅灰", "#FFB3B3B3"),
            ("深灰", "#FF4C4C4C"),
            ("红色", "#FFE81123"),
            ("橙色", "#FFCA5010"),
            ("琥珀", "#FFFF8C00"),
            ("黄色", "#FFFFD335"),
            ("草绿", "#FF6BB700"),
            ("青色", "#FF00B7C3"),
            ("天蓝", "#FF00BCF2"),
            ("蓝色", "#FF0078D4"),
            ("靛蓝", "#FF5C2D91"),
            ("紫色", "#FF8764B8"),
            ("洋红", "#FFC239B3"),
            ("粉色", "#FFE74856"),
            ("绿色", "#FF10893E"),
            ("棕色", "#FF8E562E")
        };

        foreach (var (name, hex) in presets)
        {
            var color = ParseHex(hex);
            yield return new ColorPresetOption(name, hex, new SolidColorBrush(color));
        }
    }

    private static MediaColor ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return MediaColor.FromArgb(0, 255, 255, 255);
        }

        var normalized = hex.Trim().ToUpperInvariant();
        if (!normalized.StartsWith("#", StringComparison.Ordinal))
        {
            normalized = $"#{normalized}";
        }

        if (normalized.Length == 7)
        {
            normalized = "#FF" + normalized[1..];
        }

        try
        {
            var parsed = System.Windows.Media.ColorConverter.ConvertFromString(normalized);
            if (parsed is MediaColor color)
            {
                return color;
            }
        }
        catch
        {
            // 旧配置中的颜色字符串可能无效，解析失败时使用默认值
        }

        return MediaColor.FromArgb(0, 255, 255, 255);
    }

    public sealed record ColorPresetOption(string Name, string Hex, MediaBrush Brush);
}
