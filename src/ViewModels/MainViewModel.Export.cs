using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;
using IcoConverter.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace IcoConverter.ViewModels
{
    public partial class MainViewModel
    {
        private readonly IIcoConverterService _icoConverterService;
        private readonly IIcnsConverterService _icnsConverterService;
        private readonly WebsiteIconExportService _websiteIconExportService;
        private readonly IBatchConversionService _batchConversionService;
        private IcoPngFrameMode _icoPngFrameMode = IcoPngFrameMode.Auto;
        private System.Threading.CancellationTokenSource? _cts;

        private static readonly (int Width, int Height)[] DefaultIcoResolutions = new[]
        {
            (16, 16),
            (24, 24),
            (32, 32),
            (48, 48),
            (64, 64),
            (72, 72),
            (96, 96),
            (128, 128),
            (256, 256)
        };
        private static readonly (int Width, int Height)[] OptionalPngResolutions = new[]
        {
            (512, 512),
            (1024, 1024)
        };
        // (格式, 尺寸) -> 是否勾选。512/1024 在 ICO 与 ICNS 下的默认值不同
        private readonly Dictionary<(IconExportFormat Format, int Size), bool> _highResolutionChoices = new();

        public IAsyncRelayCommand ConvertToIcoCommand { get; }
        public IAsyncRelayCommand ConvertToIcnsCommand { get; }
        public IAsyncRelayCommand ExportPreviewPngCommand { get; }
        public IAsyncRelayCommand ExportPreviewWebpCommand { get; }
        public IAsyncRelayCommand ExportPngSetCommand { get; }
        public IAsyncRelayCommand ExportWebsiteIconsCommand { get; }
        public IAsyncRelayCommand BatchConvertCommand { get; }
        public IRelayCommand CancelCommand { get; }
        public ObservableCollection<IcoResolution> AvailableResolutions { get; }
        public bool CanExportIcon => CanExportPreview();
        private bool CanExportPreview() => !IsProcessing && _originalImage != null;

        public IcoPngFrameMode IcoPngFrameMode
        {
            get => _icoPngFrameMode;
            set
            {
                if (SetProperty(ref _icoPngFrameMode, value))
                {
                    SaveSettings();
                }
            }
        }

        public ObservableCollection<IcoPngFrameMode> IcoPngFrameModeOptions { get; } =
        [
            IcoPngFrameMode.Auto,
            IcoPngFrameMode.AlwaysPng,
            IcoPngFrameMode.NeverPng
        ];

        private async Task ExportPreviewAsync(PreviewExportFormat format)
        {
            if (_originalImage == null)
            {
                return;
            }

            var isWebp = format == PreviewExportFormat.Webp;
            var displayName = isWebp ? "WebP" : "PNG";
            var extension = isWebp ? ".webp" : ".png";

            try
            {
                var webpOptions = WebpExportOptions.Default;
                if (isWebp)
                {
                    var qualityWindow = new IcoConverter.WebpQualityWindow(webpOptions)
                    {
                        Owner = GetAssociatedWindow()
                    };

                    if (qualityWindow.ShowDialog() != true)
                    {
                        return;
                    }

                    webpOptions = qualityWindow.Options;
                }

                var baseName = string.IsNullOrWhiteSpace(ImagePath)
                    ? "preview"
                    : Path.GetFileNameWithoutExtension(ImagePath);

                var initialDirectory = string.IsNullOrWhiteSpace(ImagePath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : Path.GetDirectoryName(ImagePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = isWebp ? "WebP 图片|*.webp" : "PNG 图片|*.png",
                    Title = $"导出 {displayName}",
                    FileName = $"{baseName}{extension}",
                    InitialDirectory = initialDirectory,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() == true)
                {
                    IsProcessing = true;
                    var imageToExport = await GetHighFidelityImageAsync();

                    await PreviewImageExporter.WriteAsync(imageToExport, dialog.FileName, format, webpOptions);

                    var compressionText = isWebp
                        ? (webpOptions.IsLossless ? "（无损）" : $"（有损，质量 {webpOptions.Quality}）")
                        : string.Empty;
                    AddLog($"{displayName} 导出成功{compressionText}: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                ReportError($"{displayName} 导出失败", ex);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanConvertToIco() =>
            !IsProcessing && _originalImage != null;

        private async Task ConvertToIcoAsync() => await ExportIconAsync(IconExportFormat.Ico);

        private async Task ConvertToIcnsAsync() => await ExportIconAsync(IconExportFormat.Icns);

        private async Task ExportIconAsync(IconExportFormat format)
        {
            if (_originalImage == null) return;

            var isIcns = format == IconExportFormat.Icns;
            var formatName = isIcns ? "ICNS" : "ICO";
            var extension = isIcns ? ".icns" : ".ico";

            try
            {
                if (!TrySelectResolutionsForExport(format, out var selectedResolutions))
                {
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = $"{formatName} 文件|*{extension}",
                    DefaultExt = extension,
                    FileName = "icon" + extension
                };

                if (dialog.ShowDialog() == true)
                {
                    IsProcessing = true;
                    AddLog($"开始转换到{formatName}格式...");

                    var imageToConvert = await GetHighFidelityImageAsync();

                    // 位图需跨线程使用，先行 Freeze
                    if (imageToConvert is System.Windows.Freezable f && !f.IsFrozen)
                    {
                        try
                        {
                            f.Freeze();
                        }
                        catch (InvalidOperationException ex)
                        {
                            // 记录冻结失败，后续导出仍会使用此位图。
                            Debug.WriteLine($"[Export] 冻结图像失败: {ex.Message}");
                        }
                    }

                    if (_isPreviewDownsampled)
                    {
                        AddLog("检测到预览降采样，导出已切换为原图渲染。");
                    }

                    var outPath = dialog.FileName;

                    _cts = new System.Threading.CancellationTokenSource();
                    try
                    {
                        if (isIcns)
                        {
                            var result = await _icnsConverterService.ConvertToIcnsAsync(imageToConvert, outPath, selectedResolutions, _cts.Token);
                            LogIcnsResult(result, outPath);
                        }
                        else
                        {
                            await _icoConverterService.ConvertToIcoAsync(imageToConvert, outPath, selectedResolutions, _cts.Token, IcoPngFrameMode);
                            AddLog($"ICO文件已保存: {outPath}");
                        }
                    }
                    finally
                    {
                        _cts.Dispose();
                        _cts = null;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    AddLog("转换已取消。");
                }
                else
                {
                    ReportError($"转换到{formatName}时出错", ex);
                }
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ExportPngSetAsync()
        {
            if (_originalImage == null)
            {
                return;
            }

            try
            {
                if (!TrySelectResolutionsForExport(IconExportFormat.Png, out var selectedResolutions))
                {
                    return;
                }

                var baseName = string.IsNullOrWhiteSpace(ImagePath)
                    ? "icon"
                    : Path.GetFileNameWithoutExtension(ImagePath);

                var initialDirectory = string.IsNullOrWhiteSpace(ImagePath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : Path.GetDirectoryName(ImagePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                var folderDialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择 PNG 输出文件夹",
                    InitialDirectory = initialDirectory
                };

                if (folderDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(folderDialog.FolderName))
                {
                    return;
                }

                IsProcessing = true;
                AddLog($"开始导出 PNG 图标集，共 {selectedResolutions.Count} 个尺寸...");

                var imageToExport = await GetHighFidelityImageAsync();
                if (imageToExport is System.Windows.Freezable freezable && !freezable.IsFrozen)
                {
                    try
                    {
                        freezable.Freeze();
                    }
                    catch (InvalidOperationException ex)
                    {
                        Debug.WriteLine($"[PngSet] 冻结图像失败: {ex.Message}");
                    }
                }

                var outputFolder = folderDialog.FolderName;
                var outputs = selectedResolutions.Select(size => (size, $"{baseName}-{size.Width}.png"));
                var written = await Task.Run(() => PngIconSetWriter.Write(imageToExport, outputFolder, outputs));

                AddLog($"PNG 图标集已导出：{written} 个文件 → {outputFolder}");
            }
            catch (Exception ex)
            {
                ReportError("导出 PNG 图标集失败", ex);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task ExportWebsiteIconsAsync()
        {
            if (!CanExportPreview())
            {
                return;
            }

            using var cancellation = new CancellationTokenSource();
            _cts = cancellation;
            try
            {
                IsProcessing = true;
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择网站图标的输出文件夹",
                    InitialDirectory = Path.GetDirectoryName(ImagePath) ?? desktop
                };
                if (dialog.ShowDialog(GetAssociatedWindow()) != true)
                {
                    return;
                }

                var image = await GetHighFidelityImageAsync();
                if (!image.IsFrozen)
                {
                    image.Freeze();
                }

                var folder = await _websiteIconExportService.ExportAsync(image, dialog.FolderName, cancellation.Token);
                AddLog($"网站图标已导出：{folder}");
                UpdateNotification("网站图标已导出", folder, NotificationLevel.Success);
            }
            catch (OperationCanceledException)
            {
                AddLog("网站图标导出已取消。");
            }
            catch (Exception ex)
            {
                ReportError("导出网站图标失败", ex);
            }
            finally
            {
                _cts = null;
                IsProcessing = false;
            }
        }

        private void LogIcnsResult(IcnsConversionResult result, string outPath)
        {
            AddLog($"ICNS文件已保存: {outPath}");
            AddLog($"写入 {result.EntryCount} 个元素（含 @2x 别名），覆盖 {result.WrittenSizes.Count} 档尺寸，共 {result.FileSize / 1024.0:0.#} KB。");

            if (result.SkippedSizes.Count > 0)
            {
                var skipped = string.Join("、", result.SkippedSizes.Select(size => $"{size.Width}×{size.Height}"));
                AddLog($"已跳过 ICNS 没有的档位: {skipped}。");
            }
        }

        private bool TrySelectResolutionsForExport(IconExportFormat format, out List<System.Drawing.Size> selectedResolutions)
        {
            selectedResolutions = new List<System.Drawing.Size>();

            var exportSource = _workingImage ?? _originalImage;
            var sourceSizeText = exportSource == null
                ? string.Empty
                : $"{exportSource.PixelWidth}×{exportSource.PixelHeight}";

            var pickerResolutions = AvailableResolutions
                .Select(r => IsOptionalHighResolution(r)
                    ? new IcoResolution(r.Width, r.Height, GetHighResolutionChoice(format, r.Width))
                    : r)
                .ToList();

            // 尺寸样张优先使用当前预览来展示蒙版和留白。导出使用未经预览降采样的图像。
            var resolutionPicker = new IcoResolutionPickerWindow(
                pickerResolutions,
                sourceSizeText,
                format,
                IcoPngFrameMode,
                PreviewImage ?? exportSource)
            {
                Owner = GetAssociatedWindow()
            };

            if (resolutionPicker.ShowDialog() != true)
            {
                return false;
            }

            IcoPngFrameMode = resolutionPicker.FrameMode;

            var selectedPairs = resolutionPicker.ResolutionItems
                .Where(r => r.IsSelected)
                .Select(r => (r.Width, r.Height))
                .ToHashSet();

            foreach (var resolution in AvailableResolutions)
            {
                var isSelected = selectedPairs.Contains((resolution.Width, resolution.Height));

                if (IsOptionalHighResolution(resolution))
                {
                    // 512/1024 的勾选按格式分别记录，避免 ICNS 的勾选影响 ICO 导出
                    _highResolutionChoices[(format, resolution.Width)] = isSelected;
                }
                else
                {
                    resolution.IsSelected = isSelected;
                }
            }

            var pickerSelection = resolutionPicker.ResolutionItems
                .Where(r => r.IsSelected)
                .Select(r => new System.Drawing.Size(r.Width, r.Height));

            return TryBuildSelectedResolutions(pickerSelection, format, out selectedResolutions);
        }

        private static bool IsOptionalHighResolution(IcoResolution resolution) => OptionalPngResolutions
            .Any(size => size.Width == resolution.Width && size.Height == resolution.Height);

        // 512 和 1024 超出标准 ICO 尺寸，因此 ICO 默认不勾选。
        private bool GetHighResolutionChoice(IconExportFormat format, int size) =>
            _highResolutionChoices.TryGetValue((format, size), out var choice)
                ? choice
                : format is IconExportFormat.Icns or IconExportFormat.Png;

        private void CancelProcessing()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                try
                {
                    _cts.Cancel();
                    AddLog("已请求取消处理。");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Cancel] 取消处理失败: {ex}");
                }
            }
        }

        private bool CanCancelProcessing()
        {
            return _cts != null && !_cts.IsCancellationRequested;
        }

        private bool CanBatchConvert() => !IsProcessing;

        private async Task BatchConvertAsync()
        {
            try
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.webp;*.svg;*.svgz|所有文件|*.*",
                    Title = "选择要批量转换的图片",
                    Multiselect = true
                };

                if (openDialog.ShowDialog() != true || openDialog.FileNames.Length == 0)
                {
                    return;
                }

                if (!TryBuildSelectedResolutions(out var selectedResolutions))
                {
                    return;
                }

                var batchInitialDirectory = Path.GetDirectoryName(openDialog.FileNames[0])
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                var folderDialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择输出文件夹",
                    InitialDirectory = batchInitialDirectory
                };

                if (folderDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(folderDialog.FolderName))
                {
                    return;
                }

                IsProcessing = true;
                _cts = new System.Threading.CancellationTokenSource();

                var result = await _batchConversionService.ConvertAsync(
                    openDialog.FileNames,
                    folderDialog.FolderName,
                    selectedResolutions,
                    CornerRadius,
                    CornerRadiusUnit,
                    SelectedMaskShape,
                    CircleFitMode,
                    CirclePaddingPercent,
                    EnableExportEdgeSupersampling,
                    ExpandCanvasPixels,
                    ResolveCanvasFillColor(),
                    FillCanvasOutsideMask,
                    TargetDpi,
                    SvgRenderMaxSize,
                    _cts.Token,
                    AddLog);

                AddLog($"批量转换完成。总计: {result.Total}，成功: {result.Success}，失败: {result.Failed}。");
            }
            catch (OperationCanceledException)
            {
                AddLog("批量转换已取消。");
            }
            catch (Exception ex)
            {
                ReportError("批量转换出错", ex);
            }
            finally
            {
                IsProcessing = false;
                if (_cts != null)
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }

        private bool TryBuildSelectedResolutions(
            out List<System.Drawing.Size> selectedResolutions,
            IconExportFormat format = IconExportFormat.Ico)
        {
            return TryBuildSelectedResolutions(
                AvailableResolutions.Where(r => r.IsSelected).Select(r => new System.Drawing.Size(r.Width, r.Height)),
                format,
                out selectedResolutions);
        }

        private bool TryBuildSelectedResolutions(
            IEnumerable<System.Drawing.Size> sizes,
            IconExportFormat format,
            out List<System.Drawing.Size> selectedResolutions)
        {
            selectedResolutions = sizes
                .Distinct()
                .OrderBy(size => size.Width)
                .ToList();

            if (format == IconExportFormat.Ico &&
                selectedResolutions.Any(size => size.Width > IcoResolution.IcoMaxSize || size.Height > IcoResolution.IcoMaxSize))
            {
                AddLog("已包含 512/1024 帧。ICO 目录项只能记录 256，部分旧工具可能识别不到这两帧。");
            }

            if (selectedResolutions.Count > 0)
            {
                return true;
            }

            ReportWarning("请至少选择一个尺寸。");
            return false;
        }

        private void UpdateHighResolutionOptions(BitmapSource? source)
        {
            var maxSide = source == null ? 0 : Math.Max(source.PixelWidth, source.PixelHeight);

            foreach (var size in OptionalPngResolutions)
            {
                var shouldShow = maxSide >= size.Width;
                var existing = AvailableResolutions.FirstOrDefault(r => r.Width == size.Width && r.Height == size.Height);

                if (shouldShow && existing == null)
                {
                    var insertIndex = GetResolutionInsertIndex(size.Width, size.Height);
                    AvailableResolutions.Insert(insertIndex, new IcoResolution(size.Width, size.Height, false));
                }
                else if (!shouldShow && existing != null)
                {
                    AvailableResolutions.Remove(existing);
                }
            }
        }

        private int GetResolutionInsertIndex(int width, int height)
        {
            for (int i = 0; i < AvailableResolutions.Count; i++)
            {
                var current = AvailableResolutions[i];
                if (current.Width > width)
                {
                    return i;
                }

                if (current.Width == width && current.Height >= height)
                {
                    return i;
                }
            }

            return AvailableResolutions.Count;
        }
    }
}
