using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;
using IcoConverter.Services;
using IcoConverter.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IcoConverter.ViewModels
{
    public partial class MainViewModel
    {
        private readonly IImageLoadService _imageLoadService;
        private BitmapSource? _originalImage;
        private string _imagePath = string.Empty;
        private bool _isDropHintVisible;
        private const int AutoPreviewDisableThreshold = 2048;
        private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll" };
        public IRelayCommand WindowDragOverCommand { get; }
        public IRelayCommand WindowDropCommand { get; }
        public IRelayCommand WindowDragLeaveCommand { get; }
        public IRelayCommand LoadImageCommand { get; }
        private bool CanLoadImage() => !IsProcessing;

        // SVG 光栅化时的目标长边像素数。
        private const int SvgRenderMaxSize = 1024;

        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        public bool IsDropHintVisible
        {
            get => _isDropHintVisible;
            set => SetProperty(ref _isDropHintVisible, value);
        }

        private void LoadImage()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "图片、图标或可执行文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp;*.svg;*.svgz;*.ico;*.icns;*.exe;*.dll|所有文件|*.*",
                    Title = "选择图片、图标或可执行文件"
                };

                if (dialog.ShowDialog() == true)
                {
                    ProcessImageFile(dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                ReportError("加载图片时出错", ex);
            }
        }

        public void ProcessImageFile(string filePath)
        {
            // 加载成功后转入异步蒙版流程，IsProcessing 由该流程负责复位；未进入时才在此处复位
            bool startedApply = false;
            try
            {
                filePath = ShortcutResolver.ResolveShortcutTarget(filePath);

                if (IsExecutableFile(filePath))
                {
                    OpenExecutableIconPicker(filePath);
                    return;
                }

                if (IsIconContainerFile(filePath))
                {
                    OpenIconToPngWindow(filePath);
                    return;
                }

                IsProcessing = true;
                AddLog($"处理图片: {filePath}");

                if (!_imageLoadService.IsSupportedImageFile(filePath))
                {
                    var extension = System.IO.Path.GetExtension(filePath).ToLower();
                    ReportWarning($"不支持的文件格式 '{extension}'。请选择图片文件。");
                    return;
                }

                var loadResult = _imageLoadService.LoadImage(filePath, TargetDpi, SvgRenderMaxSize);
                var normalizedBitmap = loadResult.Image;

                AddLog($"图片 DPI: {loadResult.OriginalDpiX:0.##} x {loadResult.OriginalDpiY:0.##}");
                AddLog($"加载 DPI: {normalizedBitmap.DpiX:0.##} x {normalizedBitmap.DpiY:0.##}");

                _originalImage = normalizedBitmap;
                _workingImage = normalizedBitmap;

                if (_sourceOverride != null)
                {
                    _sourceOverride = null;
                    OnPropertyChanged(nameof(HasSourceOverride));
                    OnPropertyChanged(nameof(SourceOverrideHint));
                }
                _appliedCropRegion = new CropRegion(0, 0, normalizedBitmap.PixelWidth, normalizedBitmap.PixelHeight);
                _cropSelection = _appliedCropRegion.Value.ToRect();
                _isCropModeEnabled = false;
                OnPropertyChanged(nameof(IsCropModeEnabled));
                OnPropertyChanged(nameof(IsCropOverlayVisible));
                OnPropertyChanged(nameof(PreviewContentMargin));
                NotifyCropMetricsChanged();

                _previewPipelineSource = CreatePreviewPipelineSource(normalizedBitmap, out var previewScale);
                _cropPreviewSource = CreatePreviewPipelineSource(normalizedBitmap, out _cropPreviewScale);
                var downsampled = previewScale < 0.999;
                if (_isPreviewDownsampled != downsampled)
                {
                    _isPreviewDownsampled = downsampled;
                    OnPropertyChanged(nameof(IsPreviewDownsampled));
                    OnPropertyChanged(nameof(PreviewModeHint));
                }
                _previewSourceScale = previewScale;
                PreviewImage = _previewPipelineSource;
                ImagePath = filePath;
                UpdateCornerRadiusMaximum(normalizedBitmap);
                UpdateHighResolutionOptions(normalizedBitmap);
                ResetImageTransform();
                UpdateImageTransformLimits(normalizedBitmap);


                InvalidateProcessedCaches();
                _pendingPreviewUpdate = false;
                _previewDebounceTimer.Stop();
                ResetHistory();

                NotifyCommandStateChanges();
                OnPropertyChanged(nameof(CornerRadiusHint));

                AddLog($"图片已加载: {normalizedBitmap.PixelWidth}x{normalizedBitmap.PixelHeight}");

                if (_isPreviewDownsampled)
                {
                    AddLog($"大图优化已启用：预览采用 {_previewPipelineSource?.PixelWidth}x{_previewPipelineSource?.PixelHeight} 降采样通道，导出保持原图保真。");
                }

                var currentTransform = new ImageTransformOptions(ImageScale, ImageOffsetX, ImageOffsetY);
                var shouldAutoApply = !currentTransform.IsIdentity
                    || SelectedMaskShape == MaskShape.Circle
                    || CornerRadius > 0;

                if (shouldAutoApply)
                {
                    startedApply = true;
                    _ = ApplyCornerRadiusAsync();
                }
                else
                {
                    IsProcessing = false;
                }
            }
            catch (Exception ex)
            {
                var fileName = string.IsNullOrWhiteSpace(filePath)
                    ? "所选文件"
                    : System.IO.Path.GetFileName(filePath);
                AddLog($"错误详情: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    AddLog($"错误内部信息: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }

                ReportError($"处理图片时出错（{fileName}）", ex);
            }
            finally
            {
                if (!startedApply)
                {
                    IsProcessing = false;
                }
            }
        }

        public void HandleFileDrop(string[] files)
        {
            if (files == null || files.Length == 0) return;

            var resolvedFiles = files
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(ShortcutResolver.ResolveShortcutTarget)
                .ToArray();

            if (resolvedFiles.Length == 0)
            {
                return;
            }

            var executableFile = resolvedFiles.FirstOrDefault(IsExecutableFile);
            if (!string.IsNullOrEmpty(executableFile))
            {
                OpenExecutableIconPicker(executableFile);
                return;
            }

            var iconFile = resolvedFiles.FirstOrDefault(IsIconContainerFile);
            if (!string.IsNullOrEmpty(iconFile))
            {
                OpenIconToPngWindow(iconFile);
                return;
            }

            var imageFiles = resolvedFiles.Where(_imageLoadService.IsSupportedImageFile).ToArray();

            if (imageFiles.Length > 0)
            {
                ProcessImageFile(imageFiles[0]);
            }
            else
            {
                ReportWarning("拖放的文件不是支持的文件格式。");
            }
        }

        private void HandleWindowDragOver(System.Windows.DragEventArgs? e)
        {
            if (e is null)
            {
                return;
            }

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
                IsDropHintVisible = true;
                return;
            }

            e.Effects = System.Windows.DragDropEffects.None;
            IsDropHintVisible = false;
        }

        private void HandleWindowDrop(System.Windows.DragEventArgs? e)
        {
            if (e is null)
            {
                return;
            }

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                HandleFileDrop(files);
                e.Handled = true;
            }

            IsDropHintVisible = false;
        }

        private void HandleWindowDragLeave(System.Windows.DragEventArgs? _)
        {
            IsDropHintVisible = false;
        }

        private static bool IsIconContainerFile(string filePath)
        {
            var extension = System.IO.Path.GetExtension(filePath);
            return string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(extension, ".icns", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExecutableFile(string filePath)
        {
            var extension = System.IO.Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(extension) && ExecutableExtensions.Contains(extension);
        }

        private void OpenExecutableIconPicker(string filePath)
        {
            try
            {
                AddLog($"检测到可执行/库文件: {filePath}");
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    var window = App.ServiceProvider.GetRequiredService<ExecutableIconPickerWindow>();
                    window.Owner = GetAssociatedWindow();
                    window.Initialize(filePath);
                    window.ShowDialog();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                ReportError("无法打开图标提取器", ex);
            }
        }

        private void OpenIconToPngWindow(string iconPath)
        {
            // 在拖放回调中直接弹出模态窗口会阻塞拖放状态，需以低优先级排队打开
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var window = App.ServiceProvider.GetRequiredService<IcoToPngWindow>();
                window.Initialize(iconPath);
                window.Owner = GetAssociatedWindow();
                window.ShowDialog();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
