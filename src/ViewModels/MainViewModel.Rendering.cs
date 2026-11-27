using IcoConverter.Models;
using IcoConverter.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace IcoConverter.ViewModels
{
    public partial class MainViewModel
    {
        private BitmapSource? _previewImage;
        private bool _showMaskGuides = true;
        private bool _enablePreviewEdgeSupersampling = true;
        private bool _enableExportEdgeSupersampling = true;
        private readonly DispatcherTimer _previewDebounceTimer;
        private bool _pendingPreviewUpdate;
        private const int PreviewDownsampleThreshold = 2048;
        private const int PreviewMaxLongSide = 1536;
        private RenderCacheEntry? _previewCache;
        private RenderCacheEntry? _exportCache;

        private BitmapSource? _previewPipelineSource;
        private bool _isPreviewDownsampled;
        private double _previewSourceScale = 1d;
        private int _previewPixelWidth = 1;
        private int _previewPixelHeight = 1;

        public BitmapSource? PreviewImage
        {
            get => _previewImage;
            set
            {
                if (SetProperty(ref _previewImage, value))
                {
                    UpdatePreviewGuideMetrics(value);
                    OnPropertyChanged(nameof(IsMaskGuideVisible));
                }
            }
        }

        public bool ShowMaskGuides
        {
            get => _showMaskGuides;
            set
            {
                if (SetProperty(ref _showMaskGuides, value))
                {
                    OnPropertyChanged(nameof(IsMaskGuideVisible));
                }
            }
        }

        public bool EnablePreviewEdgeSupersampling
        {
            get => _enablePreviewEdgeSupersampling;
            set
            {
                if (SetProperty(ref _enablePreviewEdgeSupersampling, value))
                {
                    OnPropertyChanged(nameof(SupersampleMode));
                    SchedulePreviewUpdate();
                }
            }
        }

        public bool EnableExportEdgeSupersampling
        {
            get => _enableExportEdgeSupersampling;
            set
            {
                if (SetProperty(ref _enableExportEdgeSupersampling, value))
                {
                    OnPropertyChanged(nameof(SupersampleMode));
                    NotifyExportSupersampleHintChanged();
                }
            }
        }

        // 将预览和导出的超采样开关映射为界面中的单个下拉选项。
        public ObservableCollection<SupersampleMode> SupersampleModeOptions { get; }

        public SupersampleMode SupersampleMode
        {
            get => (EnablePreviewEdgeSupersampling, EnableExportEdgeSupersampling) switch
            {
                (true, true) => SupersampleMode.Both,
                (true, false) => SupersampleMode.Preview,
                (false, true) => SupersampleMode.Export,
                _ => SupersampleMode.Off
            };
            set
            {
                if (SupersampleMode == value)
                {
                    return;
                }

                (EnablePreviewEdgeSupersampling, EnableExportEdgeSupersampling) = value switch
                {
                    SupersampleMode.Preview => (true, false),
                    SupersampleMode.Export => (false, true),
                    SupersampleMode.Both => (true, true),
                    _ => (false, false)
                };

                OnPropertyChanged();
            }
        }

        // 输出画布超过渲染器的超采样尺寸上限时显示提示。
        public bool IsExportSupersampleHintVisible =>
            EnableExportEdgeSupersampling && ExportCanvasLongSide > ImageProcessor.SupersampleMaxSize;

        public string ExportSupersampleHint =>
            $"输出长边 {ExportCanvasLongSide}px，超过 {ImageProcessor.SupersampleMaxSize}px 不做 2x 超采样。";

        private int ExportCanvasLongSide
        {
            get
            {
                var source = WorkingSource ?? _originalImage;
                if (source == null)
                {
                    return 0;
                }

                var (width, height) = ResolveOutputSize(source);
                return Math.Max(width, height);
            }
        }

        // 用有效图像尺寸及每边扩展量计算输出画布。
        private (int Width, int Height) ResolveOutputSize(BitmapSource source)
        {
            var maxShrink = (Math.Min(source.PixelWidth, source.PixelHeight) - 1) / 2;
            var expand = Math.Clamp(_expandCanvasPixels, -maxShrink, 4096);
            return (
                Math.Max(1, source.PixelWidth + expand * 2),
                Math.Max(1, source.PixelHeight + expand * 2));
        }

        private void NotifyExportSupersampleHintChanged()
        {
            OnPropertyChanged(nameof(IsExportSupersampleHintVisible));
            OnPropertyChanged(nameof(ExportSupersampleHint));
            OnPropertyChanged(nameof(OutputSizeText));
        }

        public bool IsPreviewDownsampled => _isPreviewDownsampled;

        public string PreviewModeHint
        {
            get
            {
                if (_isCropModeEnabled)
                {
                    return "裁剪模式：拖动裁剪框调整范围，Enter 应用";
                }

                return _isPreviewDownsampled
                    ? "预览: 降采样加速 | 导出: 原图保真"
                    : "预览/导出: 原图保真";
            }
        }

        public bool IsMaskGuideVisible => PreviewImage != null && ShowMaskGuides && !_isCropModeEnabled;

        public int PreviewPixelWidth => _previewPixelWidth;

        public int PreviewPixelHeight => _previewPixelHeight;

        public double PreviewGuideCornerRadius
        {
            get
            {
                if (!IsRoundedMaskSelected)
                {
                    return 0;
                }

                // 按导出尺寸计算后再缩放到预览，像素单位与百分比单位才能对应
                var source = WorkingSource ?? _originalImage;
                if (source == null)
                {
                    return 0;
                }

                return ResolveRoundedRadiusPixels(source.PixelWidth, source.PixelHeight) * _previewSourceScale;
            }
        }

        // 外切引导线可能超出预览边界。由预览容器裁剪超出部分。
        public double CircleGuideDiameter
        {
            get
            {
                if (!IsCircleMaskSelected)
                {
                    return 0;
                }

                var longest = (double)Math.Max(_previewPixelWidth, _previewPixelHeight);
                var shortest = (double)Math.Min(_previewPixelWidth, _previewPixelHeight);

                return CircleFitMode switch
                {
                    CircleFitMode.Circumscribed => longest,
                    CircleFitMode.Padded => shortest * (1d - Math.Clamp(CirclePaddingPercent, 0, 45) / 100d),
                    _ => shortest
                };
            }
        }

        // 外切时追加内切参考线，用于对比圆的裁切比例
        public bool IsCircleReferenceVisible =>
            IsCircleMaskSelected && CircleFitMode == CircleFitMode.Circumscribed && !IsSquareCanvas;

        public double CircleReferenceDiameter => Math.Min(_previewPixelWidth, _previewPixelHeight);

        private bool CanApplyCornerRadius() => !IsProcessing && _originalImage != null;

        private void SchedulePreviewUpdate()
        {
            if (_originalImage == null)
            {
                return;
            }

            _pendingPreviewUpdate = true;
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }

        private async void PreviewDebounceTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _previewDebounceTimer.Stop();

                if (!_pendingPreviewUpdate)
                {
                    return;
                }

                if (IsProcessing)
                {
                    _previewDebounceTimer.Start();
                    return;
                }

                _pendingPreviewUpdate = false;

                if (CanApplyCornerRadius())
                {
                    await ApplyCornerRadiusAsync();
                }
            }
            catch (Exception ex)
            {
                // 在计时器回调内记录异常，避免它进入 Dispatcher 的全局异常处理。
                _pendingPreviewUpdate = false;
                Debug.WriteLine($"[PreviewDebounce] 预览刷新失败: {ex}");
                AddLog($"预览刷新失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task ApplyCornerRadiusAsync()
        {
            if (_originalImage == null) return;

            // 裁剪模式下预览显示完整原图，暂不应用蒙版
            if (_isCropModeEnabled)
            {
                PreviewImage = _cropPreviewSource ?? WorkingSource;
                return;
            }

            var previewSource = _previewPipelineSource ?? WorkingSource ?? _originalImage;

            try
            {
                IsProcessing = true;
                var maskSource = _workingImage ?? _originalImage;
                var options = CreateRenderOptions(maskSource, EnablePreviewEdgeSupersampling);
                var previewCanvasExpandPixels = ScaleLengthForPreview(options.CanvasExpandPixels);
                var canvasFillMode = CanvasFillMode;
                var canvasCustomColorHex = NormalizeCanvasColorHex(CanvasCustomColorHex);
                var shape = options.Shape;
                var radius = options.Radius;

                if (shape == MaskShape.RoundedRectangle && CornerRadiusUnit == RadiusUnit.Pixels && radius != _cornerRadiusPixels)
                {
                    // 直接写字段：经 setter 会再次排队预览并多压入一条撤销记录
                    _suspendUndoCapture = true;
                    _cornerRadiusPixels = radius;
                    _suspendUndoCapture = false;
                    OnPropertyChanged(nameof(CornerRadius));
                    OnPropertyChanged(nameof(CornerRadiusHint));
                    UpdateRadiusPresetSelection();
                    NotifyPreviewGuideChanged();
                    AddLog($"圆角半径已调整为 {radius}px（受图片尺寸限制）。");
                }

                var exportTransformOptions = new ImageTransformOptions(ImageScale, ImageOffsetX, ImageOffsetY);
                // 预览可能经过降采样，所有以像素为单位的参数都使用相同的比例。
                options = options with
                {
                    Radius = ScaleRadiusForPreview(radius),
                    Transform = BuildPreviewTransformOptions(exportTransformOptions),
                    CanvasExpandPixels = previewCanvasExpandPixels
                };
                var cacheKey = new RenderCacheKey(previewSource, options, canvasFillMode, canvasCustomColorHex);
                if (_previewCache is { } cached && cached.Key.Matches(cacheKey))
                {
                    PreviewImage = cached.Image;
                    AddLog("蒙版参数未变化，已复用缓存预览。");
                    return;
                }

                if (!options.RequiresProcessing)
                {
                    PreviewImage = previewSource;
                    AddLog("当前蒙版配置无需裁剪，已使用原图预览。");
                    _previewCache = new RenderCacheEntry(cacheKey, previewSource);
                    return;
                }

                var requiresMask = options.Radius > 0;
                if (requiresMask && !options.Transform.IsIdentity)
                {
                    AddLog($"应用蒙版与图像变换: {shape}，半径 {radius}px，缩放 {exportTransformOptions.Scale:0.##}x，偏移 ({exportTransformOptions.OffsetX:0.#}, {exportTransformOptions.OffsetY:0.#})px");
                }
                else if (requiresMask)
                {
                    AddLog($"应用蒙版: {shape}，半径 {radius}px");
                }
                else
                {
                    AddLog($"应用图像变换: 缩放 {exportTransformOptions.Scale:0.##}x，偏移 ({exportTransformOptions.OffsetX:0.#}, {exportTransformOptions.OffsetY:0.#})px");
                }

                var stopwatch = Stopwatch.StartNew();

                // 图像处理在后台线程执行，UI 线程仅接收结果
                BitmapSource imageToProcess = previewSource;
                if (imageToProcess is DispatcherObject dobj && dobj.CheckAccess())
                {
                    if (imageToProcess.CanFreeze)
                        imageToProcess.Freeze();
                }

                var processedImage = await Task.Run(() => _imageProcessor.ApplyMask(imageToProcess, options));

                stopwatch.Stop();

                PreviewImage = processedImage;
                _previewCache = new RenderCacheEntry(cacheKey, processedImage);

                if (_isPreviewDownsampled)
                {
                    AddLog($"蒙版预览完成（降采样模式），耗时 {stopwatch.ElapsedMilliseconds}ms，预览输出 {processedImage.PixelWidth}x{processedImage.PixelHeight}");
                }
                else
                {
                    AddLog($"蒙版应用完成，耗时 {stopwatch.ElapsedMilliseconds}ms，输出 {processedImage.PixelWidth}x{processedImage.PixelHeight}");
                }

                AddHistoryEntry($"预览完成 - {DescribeMaskState(CreateMaskStateSnapshot())}");
            }
            catch (Exception ex)
            {
                AddLog($"应用蒙版时出错: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private BitmapSource CreatePreviewPipelineSource(BitmapSource source, out double scale)
        {
            scale = 1d;

            var longestSide = Math.Max(source.PixelWidth, source.PixelHeight);
            if (longestSide <= PreviewDownsampleThreshold)
            {
                return source;
            }

            scale = Math.Min(1d, PreviewMaxLongSide / (double)longestSide);
            if (scale >= 0.999)
            {
                scale = 1d;
                return source;
            }

            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            if (transformed.CanFreeze)
            {
                transformed.Freeze();
            }

            return transformed;
        }

        private int ScaleRadiusForPreview(int exportRadius)
        {
            if (!_isPreviewDownsampled)
            {
                return exportRadius;
            }

            return Math.Max(0, (int)Math.Round(exportRadius * _previewSourceScale, MidpointRounding.AwayFromZero));
        }

        // 画布扩展等长度参数同为像素值，预览降采样时需同比缩放
        private int ScaleLengthForPreview(int pixels)
        {
            if (!_isPreviewDownsampled || pixels == 0)
            {
                return pixels;
            }

            var scaled = (int)Math.Round(pixels * _previewSourceScale, MidpointRounding.AwayFromZero);
            if (scaled == 0)
            {
                // 缩放后取整可能为 0，保留 1px 以保证边框可见
                return Math.Sign(pixels);
            }

            return scaled;
        }

        private ImageTransformOptions BuildPreviewTransformOptions(ImageTransformOptions exportTransformOptions)
        {
            if (!_isPreviewDownsampled)
            {
                return exportTransformOptions;
            }

            return new ImageTransformOptions(
                exportTransformOptions.Scale,
                exportTransformOptions.OffsetX * _previewSourceScale,
                exportTransformOptions.OffsetY * _previewSourceScale);
        }

        private void RebuildPreviewPipeline(BitmapSource source)
        {
            _previewPipelineSource = CreatePreviewPipelineSource(source, out var previewScale);
            _previewSourceScale = previewScale;

            var downsampled = previewScale < 0.999;
            if (_isPreviewDownsampled != downsampled)
            {
                _isPreviewDownsampled = downsampled;
                OnPropertyChanged(nameof(IsPreviewDownsampled));
            }

            OnPropertyChanged(nameof(PreviewModeHint));
            NotifyExportSupersampleHintChanged();
            NotifyPreviewGuideChanged();
        }

        private void InvalidateProcessedCaches()
        {
            _previewCache = null;
            _exportCache = null;
        }

        private void NotifyPreviewGuideChanged()
        {
            OnPropertyChanged(nameof(PreviewGuideCornerRadius));
            OnPropertyChanged(nameof(CircleGuideDiameter));
            OnPropertyChanged(nameof(CircleReferenceDiameter));
            OnPropertyChanged(nameof(IsCircleReferenceVisible));
            OnPropertyChanged(nameof(IsCircleFitHintVisible));
            OnPropertyChanged(nameof(CornerRadiusHint));
        }

        private void UpdatePreviewGuideMetrics(BitmapSource? image)
        {
            var width = image?.PixelWidth ?? 1;
            var height = image?.PixelHeight ?? 1;

            if (_previewPixelWidth != width)
            {
                _previewPixelWidth = width;
                OnPropertyChanged(nameof(PreviewPixelWidth));
            }

            if (_previewPixelHeight != height)
            {
                _previewPixelHeight = height;
                OnPropertyChanged(nameof(PreviewPixelHeight));
            }

            NotifyPreviewGuideChanged();
        }

        private async Task<BitmapSource> GetHighFidelityImageAsync()
        {
            var exportSource = WorkingSource ?? _originalImage;
            if (exportSource == null)
            {
                throw new InvalidOperationException("当前没有可导出的图像。");
            }

            var options = CreateRenderOptions(exportSource, EnableExportEdgeSupersampling);
            if (!options.RequiresProcessing)
            {
                return exportSource;
            }

            var cacheKey = new RenderCacheKey(
                exportSource, options, CanvasFillMode, NormalizeCanvasColorHex(CanvasCustomColorHex));
            if (_exportCache is { } cached && cached.Key.Matches(cacheKey))
            {
                return cached.Image;
            }

            BitmapSource imageToProcess = exportSource;
            if (imageToProcess is DispatcherObject dobj && dobj.CheckAccess() && imageToProcess.CanFreeze)
            {
                imageToProcess.Freeze();
            }

            var processedImage = await Task.Run(() => _imageProcessor.ApplyMask(imageToProcess, options));
            _exportCache = new RenderCacheEntry(cacheKey, processedImage);

            return processedImage;
        }

        private ImageRenderOptions CreateRenderOptions(BitmapSource source, bool enableEdgeSupersampling)
        {
            var shape = SelectedMaskShape;
            return new ImageRenderOptions(
                shape,
                ResolveMaskRadiusPixels(shape, source.PixelWidth, source.PixelHeight),
                new ImageTransformOptions(ImageScale, ImageOffsetX, ImageOffsetY),
                CircleFitMode,
                CirclePaddingPercent,
                enableEdgeSupersampling,
                ExpandCanvasPixels,
                ResolveCanvasFillColor(),
                FillCanvasOutsideMask);
        }

        private sealed record RenderCacheEntry(RenderCacheKey Key, BitmapSource Image);

        private readonly record struct RenderCacheKey(
            BitmapSource Source,
            ImageRenderOptions Options,
            CanvasFillMode FillMode,
            string CustomColorHex)
        {
            public bool Matches(RenderCacheKey other) =>
                ReferenceEquals(Source, other.Source) &&
                Options.Radius == other.Options.Radius &&
                Options.Shape == other.Options.Shape &&
                Math.Abs(Options.Transform.Scale - other.Options.Transform.Scale) < 0.0001 &&
                Math.Abs(Options.Transform.OffsetX - other.Options.Transform.OffsetX) < 0.01 &&
                Math.Abs(Options.Transform.OffsetY - other.Options.Transform.OffsetY) < 0.01 &&
                Options.CircleFitMode == other.Options.CircleFitMode &&
                Options.CirclePaddingPercent == other.Options.CirclePaddingPercent &&
                Options.EnableEdgeSupersampling == other.Options.EnableEdgeSupersampling &&
                Options.CanvasExpandPixels == other.Options.CanvasExpandPixels &&
                Options.FillCanvasOutsideMask == other.Options.FillCanvasOutsideMask &&
                FillMode == other.FillMode &&
                string.Equals(CustomColorHex, other.CustomColorHex, StringComparison.OrdinalIgnoreCase);
        }
    }
}
