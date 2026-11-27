using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace IcoConverter.ViewModels
{
    public partial class MainViewModel
    {
        private int _cornerRadiusPixels = 20;
        private int _cornerRadiusPercent = 20;
        private RadiusUnit _cornerRadiusUnit = RadiusUnit.Pixels;
        private CircleFitMode _circleFitMode = CircleFitMode.Inscribed;
        private int _circlePaddingPercent = 12;
        private int _expandCanvasPixels;
        private CanvasFillMode _canvasFillMode = CanvasFillMode.Transparent;
        private string _canvasCustomColorHex = "#00FFFFFF";
        private bool _fillCanvasOutsideMask;

        private MaskShape _selectedMaskShape = MaskShape.RoundedRectangle;

        private int _cornerRadiusMaximum = DefaultCornerRadiusMaximum;

        private const int DefaultCornerRadiusMaximum = 1024;
        private const int MinimumCornerRadiusMaximum = 32;
        private const int DefaultCornerRadiusPixels = 20;
        private const int DefaultCirclePaddingPercent = 12;
        private const int ExpandCanvasSliderLimit = 1024;
        private const int RecommendedCornerRadiusHint = 96;

        public IRelayCommand<int> ApplyRadiusPresetCommand { get; }
        public IRelayCommand<MaskShape> SelectMaskShapeCommand { get; }
        public IRelayCommand ResetMaskCommand { get; }
        public IRelayCommand PickCanvasCustomColorCommand { get; }

        public int CornerRadius
        {
            get => CornerRadiusUnit == RadiusUnit.Percent ? _cornerRadiusPercent : _cornerRadiusPixels;
            set
            {
                var safeValue = Math.Clamp(value, 0, CornerRadiusMaximum);
                var snapshot = CreateMaskStateSnapshot();

                var changed = CornerRadiusUnit == RadiusUnit.Percent
                    ? SetProperty(ref _cornerRadiusPercent, safeValue)
                    : SetProperty(ref _cornerRadiusPixels, safeValue);

                if (changed)
                {
                    RegisterUndoSnapshot(snapshot);
                    OnPropertyChanged(nameof(CornerRadiusHint));
                    UpdateRadiusPresetSelection();
                    NotifyPreviewGuideChanged();
                    SchedulePreviewUpdate();
                }
            }
        }

        public int CornerRadiusMaximum
        {
            get => CornerRadiusUnit == RadiusUnit.Percent ? 100 : _cornerRadiusMaximum;
            private set
            {
                if (SetProperty(ref _cornerRadiusMaximum, Math.Max(0, value)))
                {
                    OnPropertyChanged(nameof(CornerRadiusMaximum));
                }
            }
        }

        public ObservableCollection<RadiusUnit> RadiusUnitOptions { get; }

        public ObservableCollection<CircleFitMode> CircleFitModeOptions { get; }

        public ObservableCollection<CanvasFillMode> CanvasFillModeOptions { get; }


        public ObservableCollection<RadiusPresetOption> RadiusPresets { get; }

        public RadiusUnit CornerRadiusUnit
        {
            get => _cornerRadiusUnit;
            set
            {
                if (_cornerRadiusUnit == value)
                {
                    return;
                }

                var snapshot = CreateMaskStateSnapshot();
                ConvertCornerRadiusUnit(value);

                if (SetProperty(ref _cornerRadiusUnit, value))
                {
                    RegisterUndoSnapshot(snapshot);
                    RefreshRadiusPresets();
                    OnPropertyChanged(nameof(CornerRadius));
                    OnPropertyChanged(nameof(CornerRadiusMaximum));
                    OnPropertyChanged(nameof(CornerRadiusTickFrequency));
                    OnPropertyChanged(nameof(CornerRadiusHint));
                    OnPropertyChanged(nameof(IsCornerRadiusHintVisible));
                    NotifyPreviewGuideChanged();
                    SchedulePreviewUpdate();
                }
            }
        }

        // 切换单位时同步换算数值，否则 20px 切换为百分比后会变成 20%
        private void ConvertCornerRadiusUnit(RadiusUnit target)
        {
            // 撤销恢复时两组值均已正确，再次换算会破坏其一致性
            if (_isRestoringState)
            {
                return;
            }

            var source = WorkingSource ?? _originalImage;
            if (source == null)
            {
                return;
            }

            var shortestHalf = Math.Max(1, Math.Min(source.PixelWidth, source.PixelHeight) / 2);

            if (target == RadiusUnit.Percent)
            {
                _cornerRadiusPercent = Math.Clamp(
                    (int)Math.Round(_cornerRadiusPixels * 100d / shortestHalf, MidpointRounding.AwayFromZero),
                    0,
                    100);
            }
            else
            {
                _cornerRadiusPixels = Math.Clamp(
                    (int)Math.Round(shortestHalf * _cornerRadiusPercent / 100d, MidpointRounding.AwayFromZero),
                    0,
                    shortestHalf);
            }
        }

        public int CornerRadiusTickFrequency => CornerRadiusUnit == RadiusUnit.Percent ? 5 : 4;

        // 像素模式下数字框已显示当前值，提示仅在百分比换算为像素时有意义
        public bool IsCornerRadiusHintVisible => CornerRadiusUnit == RadiusUnit.Percent;

        public string CornerRadiusHint
        {
            get
            {
                if (CornerRadiusUnit == RadiusUnit.Pixels)
                {
                    return $"当前值: {CornerRadius}px";
                }

                var source = WorkingSource ?? _previewPipelineSource;
                if (source == null)
                {
                    return $"当前值: {CornerRadius}%";
                }

                var radiusPx = ResolveRoundedRadiusPixels(source.PixelWidth, source.PixelHeight);
                return CornerRadius >= 100
                    ? $"当前值: 100% (约 {radiusPx}px，正圆)"
                    : $"当前值: {CornerRadius}% (约 {radiusPx}px)";
            }
        }

        public MaskShape SelectedMaskShape
        {
            get => _selectedMaskShape;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                if (SetProperty(ref _selectedMaskShape, value))
                {
                    RegisterUndoSnapshot(snapshot);
                    OnPropertyChanged(nameof(IsRoundedMaskSelected));
                    OnPropertyChanged(nameof(IsCircleMaskSelected));
                    OnPropertyChanged(nameof(IsCirclePaddingVisible));
                    OnPropertyChanged(nameof(IsCircleFitHintVisible));
                    NotifyPreviewGuideChanged();
                    SchedulePreviewUpdate();
                }
            }
        }

        public bool IsRoundedMaskSelected => SelectedMaskShape == MaskShape.RoundedRectangle;

        public bool IsCircleMaskSelected => SelectedMaskShape == MaskShape.Circle;

        public CircleFitMode CircleFitMode
        {
            get => _circleFitMode;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                if (SetProperty(ref _circleFitMode, value))
                {
                    RegisterUndoSnapshot(snapshot);
                    OnPropertyChanged(nameof(IsCirclePaddingVisible));
                    OnPropertyChanged(nameof(IsCircleFitHintVisible));
                    NotifyPreviewGuideChanged();
                    SchedulePreviewUpdate();
                }
            }
        }

        public bool IsCirclePaddingVisible => IsCircleMaskSelected && CircleFitMode == CircleFitMode.Padded;

        // 正方形图像上内切与外切生成的圆完全相同，需提示以免被误认为未生效
        public bool IsCircleFitHintVisible =>
            IsCircleMaskSelected && CircleFitMode == CircleFitMode.Circumscribed && IsSquareCanvas;

        public string CircleFitHint => "当前图片是正方形，内切和外切的结果相同。";

        public int CirclePaddingPercent
        {
            get => _circlePaddingPercent;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var safeValue = Math.Clamp(value, 0, 45);
                if (SetProperty(ref _circlePaddingPercent, safeValue))
                {
                    RegisterUndoSnapshot(snapshot);
                    NotifyPreviewGuideChanged();
                    SchedulePreviewUpdate();
                }
            }
        }

        public int ExpandCanvasPixels
        {
            get => _expandCanvasPixels;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                // 扩展量按每边计算。收缩时确保短边至少保留 1 像素。
                var source = WorkingSource ?? _originalImage;
                var minValue = source == null
                    ? -4096
                    : -(Math.Min(source.PixelWidth, source.PixelHeight) - 1) / 2;
                var safeValue = Math.Clamp(value, minValue, 4096);
                if (SetProperty(ref _expandCanvasPixels, safeValue))
                {
                    RegisterUndoSnapshot(snapshot);
                    OnPropertyChanged(nameof(ExpandCanvasSliderValue));
                    NotifyExportSupersampleHintChanged();
                    SchedulePreviewUpdate();
                }
            }
        }

        // 滑块范围为 ±1024，数字框可输入更大值。超出时滑块停在端点，不回写数值
        public double ExpandCanvasSliderValue
        {
            get => Math.Clamp(_expandCanvasPixels, -ExpandCanvasSliderLimit, ExpandCanvasSliderLimit);
            set => ExpandCanvasPixels = (int)Math.Round(value);
        }

        public bool IsOutputSizeVisible => (_workingImage ?? _originalImage) != null;

        public string OutputSizeText
        {
            get
            {
                var source = WorkingSource ?? _originalImage;
                if (source == null)
                {
                    return string.Empty;
                }

                var (width, height) = ResolveOutputSize(source);
                return $"输出 {width} × {height} px";
            }
        }

        public CanvasFillMode CanvasFillMode
        {
            get => _canvasFillMode;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                if (SetProperty(ref _canvasFillMode, value))
                {
                    RegisterUndoSnapshot(snapshot);
                    OnPropertyChanged(nameof(IsCanvasCustomColorVisible));
                    OnPropertyChanged(nameof(IsCanvasFillColorChosen));
                    SchedulePreviewUpdate();
                }
            }
        }

        public bool IsCanvasCustomColorVisible => CanvasFillMode == CanvasFillMode.Custom;

        public bool IsCanvasFillColorChosen => CanvasFillMode != CanvasFillMode.Transparent;

        // 默认背景色仅填充蒙版内部，勾选后蒙版外区域同样填充实色
        public bool FillCanvasOutsideMask
        {
            get => _fillCanvasOutsideMask;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                if (SetProperty(ref _fillCanvasOutsideMask, value))
                {
                    RegisterUndoSnapshot(snapshot);
                    SchedulePreviewUpdate();
                }
            }
        }

        public string CanvasCustomColorHex
        {
            get => _canvasCustomColorHex;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var safeValue = NormalizeCanvasColorHex(value);
                if (SetProperty(ref _canvasCustomColorHex, safeValue))
                {
                    RegisterUndoSnapshot(snapshot);
                    OnPropertyChanged(nameof(CanvasCustomColorPreviewBrush));
                    SchedulePreviewUpdate();
                }
            }
        }

        public MediaBrush CanvasCustomColorPreviewBrush => new SolidColorBrush(ResolveCustomCanvasColor(CanvasCustomColorHex));

        private int ResolveRoundedRadiusPixels(int sourceWidth, int sourceHeight)
        {
            var shortestHalf = Math.Max(0, Math.Min(sourceWidth, sourceHeight) / 2);

            if (CornerRadiusUnit == RadiusUnit.Percent)
            {
                var percent = Math.Clamp(_cornerRadiusPercent, 0, 100);
                return (int)Math.Round(shortestHalf * (percent / 100d), MidpointRounding.AwayFromZero);
            }

            return Math.Clamp(_cornerRadiusPixels, 0, shortestHalf);
        }

        private int ResolveCircleRadiusPixels(int sourceWidth, int sourceHeight)
        {
            var shortestHalf = Math.Max(0, Math.Min(sourceWidth, sourceHeight) / 2);

            return CircleFitMode switch
            {
                CircleFitMode.Circumscribed => Math.Max(0, Math.Max(sourceWidth, sourceHeight) / 2),
                CircleFitMode.Padded => (int)Math.Round(shortestHalf * (1d - Math.Clamp(CirclePaddingPercent, 0, 45) / 100d), MidpointRounding.AwayFromZero),
                _ => shortestHalf
            };
        }

        private int ResolveMaskRadiusPixels(MaskShape shape, int sourceWidth, int sourceHeight)
        {
            return shape switch
            {
                MaskShape.RoundedRectangle => ResolveRoundedRadiusPixels(sourceWidth, sourceHeight),
                MaskShape.Circle => ResolveCircleRadiusPixels(sourceWidth, sourceHeight),
                _ => ResolveRoundedRadiusPixels(sourceWidth, sourceHeight)
            };
        }

        private MediaColor ResolveCanvasFillColor()
        {
            return CanvasFillMode switch
            {
                CanvasFillMode.White => Colors.White,
                CanvasFillMode.Black => Colors.Black,
                CanvasFillMode.Custom => ResolveCustomCanvasColor(CanvasCustomColorHex),
                _ => Colors.Transparent
            };
        }

        private static MediaColor ResolveCustomCanvasColor(string? colorHex)
        {
            var normalized = NormalizeCanvasColorHex(colorHex);
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
                // 颜色字符串解析失败时按透明处理，不为此报错
            }

            return Colors.Transparent;
        }

        private static string NormalizeCanvasColorHex(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "#00FFFFFF";
            }

            var normalized = value.Trim().ToUpperInvariant();
            return normalized.StartsWith("#", StringComparison.Ordinal) ? normalized : $"#{normalized}";
        }

        private void RefreshRadiusPresets()
        {
            RadiusPresets.Clear();

            var values = CornerRadiusUnit == RadiusUnit.Percent
                ? new[] { 0, 25, 50, 75, 100 }
                : new[] { 0, 8, 16, 24, 32 };

            foreach (var value in values)
            {
                var label = CornerRadiusUnit == RadiusUnit.Percent ? $"{value}%" : value.ToString();
                RadiusPresets.Add(new RadiusPresetOption(value, label));
            }

            UpdateRadiusPresetSelection();
        }

        private void UpdateRadiusPresetSelection()
        {
            foreach (var preset in RadiusPresets)
            {
                preset.IsActive = preset.Value == CornerRadius;
            }
        }

        private void ApplyRadiusPreset(int value)
        {
            CornerRadius = value;
        }

        // 将多个默认参数的恢复合并成一条撤销记录。
        private void ResetMaskParameters()
        {
            var snapshot = CreateMaskStateSnapshot();
            _suspendUndoCapture = true;
            try
            {
                SelectedMaskShape = MaskShape.RoundedRectangle;
                CornerRadiusUnit = RadiusUnit.Pixels;
                CornerRadius = DefaultCornerRadiusPixels;
                CircleFitMode = CircleFitMode.Inscribed;
                CirclePaddingPercent = DefaultCirclePaddingPercent;
                ExpandCanvasPixels = 0;
                CanvasFillMode = CanvasFillMode.Transparent;
                FillCanvasOutsideMask = false;
                ImageScale = 1d;
                ImageOffsetX = 0d;
                ImageOffsetY = 0d;
            }
            finally
            {
                _suspendUndoCapture = false;
            }

            RegisterUndoSnapshot(snapshot);
            AddLog("蒙版参数已重置。");
        }

        private void PickCanvasCustomColor()
        {
            var picker = new IcoConverter.CanvasColorPickerWindow(CanvasCustomColorHex)
            {
                Owner = GetAssociatedWindow()
            };

            if (picker.ShowDialog() != true)
            {
                return;
            }

            CanvasCustomColorHex = picker.SelectedColorHex;
        }

        private void UpdateCornerRadiusMaximum(BitmapSource? source)
        {
            var previousMax = CornerRadiusMaximum;

            if (source == null)
            {
                CornerRadiusMaximum = Math.Max(MinimumCornerRadiusMaximum, DefaultCornerRadiusMaximum);
            }
            else
            {
                var shortestSide = Math.Min(source.PixelWidth, source.PixelHeight);
                var half = Math.Max(0d, shortestSide / 2d);
                var dynamicMax = (int)Math.Round(half, MidpointRounding.AwayFromZero);
                CornerRadiusMaximum = Math.Max(MinimumCornerRadiusMaximum, dynamicMax);
            }

            if (_cornerRadiusPixels > _cornerRadiusMaximum)
            {
                _suspendUndoCapture = true;
                _cornerRadiusPixels = _cornerRadiusMaximum;
                _suspendUndoCapture = false;
                OnPropertyChanged(nameof(CornerRadius));
            }

            if (CornerRadiusMaximum != previousMax)
            {
                var recommended = Math.Min(CornerRadiusMaximum, RecommendedCornerRadiusHint);
                AddLog($"圆角上限已自动调整为 {CornerRadiusMaximum}px，推荐值不超过 {recommended}px 以兼顾边角细节。");
            }

            OnPropertyChanged(nameof(CornerRadiusMaximum));
            OnPropertyChanged(nameof(CornerRadiusHint));
            UpdateRadiusPresetSelection();
            NotifyPreviewGuideChanged();
        }

        public sealed class RadiusPresetOption(int value, string label) : ObservableObject
        {
            private bool _isActive;

            public int Value { get; } = value;

            public string Label { get; } = label;

            public bool IsActive
            {
                get => _isActive;
                set => SetProperty(ref _isActive, value);
            }
        }
    }
}
