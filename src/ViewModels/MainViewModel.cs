using IcoConverter.Models;
using IcoConverter.Services;
using IcoConverter.Utils;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;
using IcoConverter;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Microsoft.Win32;

namespace IcoConverter.ViewModels
{
    /// <summary>
    /// 主视图模型，负责处理图像加载、圆角应用以及 ICO 转换的命令与状态。
    /// </summary>
    public class MainViewModel : ViewModelBase
    {


        private readonly Uri _lightThemeUri = new("Styles/Theme/BaseLight.xaml", UriKind.Relative);
        private readonly Uri _darkThemeUri = new("Styles/Theme/BaseDark.xaml", UriKind.Relative);
        private bool _isDarkTheme;

        private readonly IImageProcessor _imageProcessor;
        private readonly IIcoConverterService _icoConverterService;
        private readonly ILogService _logService;
        private readonly ISettingsService _settingsService;
        private readonly IImageLoadService _imageLoadService;
        private readonly IBatchConversionService _batchConversionService;

        private string _imagePath = string.Empty;
        private BitmapSource? _previewImage;
        private BitmapSource? _originalImage;
        private int _cornerRadius = 20;
        private CornerQuality _selectedCornerQuality = CornerQuality.High;
        private string _logText = string.Empty;
        private bool _isProcessing = false;
        private System.Threading.CancellationTokenSource? _cts;
        private string _statusMessage = "就绪";
        private string _title = "IcoConverter - 图片转ICO工具";
        private AppSettings _settings = new();
        private BitmapSource? _lastProcessedSource;
        private BitmapSource? _lastProcessedImage;
        private int _lastProcessedRadius = -1;
        private CornerQuality _lastProcessedQuality = CornerQuality.High;
        // SVG 渲染的最大边长（像素），用于保证输出清晰度
        private const int SvgRenderMaxSize = 1024;
        // 统一图像 DPI 的目标值（可调整）
        private double _targetDpi = 96d;
        /// <summary>
        /// 切换浅色/深色主题的命令。
        /// </summary>
        public RelayCommand ChangeThemeCommand { get; }
        /// <summary>
        /// 打开关于窗口的命令。
        /// </summary>
        public RelayCommand OpenAboutCommand { get; }
        // 窗口控制命令
        /// <summary>
        /// 最小化窗口的命令（绑定到 UI）
        /// </summary>
        public RelayCommand MinimizeCommand { get; }

        /// <summary>
        /// 关闭窗口的命令（绑定到 UI）
        /// </summary>
        public RelayCommand CloseCommand { get; }

        /// <summary>
        /// 鼠标左键按下命令（用于在窗口标题栏实现拖动）
        /// </summary>
        public RelayCommand MouseLeftButtonDownCommand { get; }
        /// <summary>
        /// 打开GitHub页面的命令。
        /// </summary>
        public RelayCommand OpenGithubCommand { get; }

        public MainViewModel(
            IImageProcessor imageProcessor,
            IIcoConverterService icoConverterService,
            ILogService logService,
            ISettingsService settingsService,
            IImageLoadService imageLoadService,
            IBatchConversionService batchConversionService)
        {
            _imageProcessor = imageProcessor;
            _icoConverterService = icoConverterService;
            _logService = logService;
            _settingsService = settingsService;
            _imageLoadService = imageLoadService;
            _batchConversionService = batchConversionService;
            _logService.LogUpdated += OnLogUpdated;

            LoadImageCommand = new RelayCommand(LoadImage, CanLoadImage);
            ApplyCornerRadiusCommand = new AsyncRelayCommand(ApplyCornerRadiusAsync, CanApplyCornerRadius);
            ConvertToIcoCommand = new AsyncRelayCommand(ConvertToIcoAsync, CanConvertToIco);
            ExportPreviewToPngCommand = new RelayCommand(ExportPreviewToPng, CanExportPreviewToPng);
            BatchConvertCommand = new AsyncRelayCommand(BatchConvertAsync, CanBatchConvert);
            CancelCommand = new RelayCommand(CancelProcessing, CanCancelProcessing);
            ClearLogCommand = new RelayCommand(ClearLog);
            ChangeThemeCommand = new RelayCommand(ChangeTheme);
            OpenAboutCommand = new RelayCommand(_ => OpenAboutWindow());
            MinimizeCommand = new RelayCommand(_ => MinimizeWindow());
            CloseCommand = new RelayCommand(_ => CloseWindow());
            MouseLeftButtonDownCommand = new RelayCommand(parameter =>
            {
                if (parameter is System.Windows.Input.MouseButtonEventArgs e && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                {
                    DragWindow();
                }
            });
            OpenGithubCommand = new RelayCommand(_ =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/fallssyj/IcoConverter",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show($"无法打开浏览器: {ex.Message}",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
            // 初始化可选分辨率列表
            AvailableResolutions =
            [
                new IcoResolution(16, 16, true),
                new IcoResolution(24, 24, true),
                new IcoResolution(32, 32, true),
                new IcoResolution(48, 48, true),
                new IcoResolution(64, 64, true),
                new IcoResolution(72, 72, true),
                new IcoResolution(96, 96, true),
                new IcoResolution(128, 128, true),
                new IcoResolution(256, 256, true)
            ];

            // 初始化圆角质量选项
            CornerQualityOptions =
            [
                CornerQuality.Low,
                CornerQuality.Medium,
                CornerQuality.High
            ];

            LoadSettings();
            ApplyTheme(_isDarkTheme);
            CommandManager.InvalidateRequerySuggested();

            // 初始化日志显示
            UpdateLogState(_logService.LogText, _logService.StatusMessage);

            AddLog("IcoConverter 已启动。请拖放图片或点击加载图片按钮。");
        }

        public string ImagePath
        {
            get => _imagePath;
            set => SetField(ref _imagePath, value);
        }

        public BitmapSource? PreviewImage
        {
            get => _previewImage;
            set => SetField(ref _previewImage, value);
        }

        public int CornerRadius
        {
            get => _cornerRadius;
            set
            {
                var safeValue = Math.Max(0, value);
                SetField(ref _cornerRadius, safeValue);
            }
        }

        public CornerQuality SelectedCornerQuality
        {
            get => _selectedCornerQuality;
            set => SetField(ref _selectedCornerQuality, value);
        }

        public ObservableCollection<CornerQuality> CornerQualityOptions { get; }

        public string LogText
        {
            get => _logText;
            set => SetField(ref _logText, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetField(ref _isProcessing, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                    UpdateStatusMessage();
                }
            }
        }

        private bool _isDropHintVisible;
        /// <summary>
        /// 是否显示拖放提示。
        /// </summary>
        public bool IsDropHintVisible
        {
            get => _isDropHintVisible;
            set => SetField(ref _isDropHintVisible, value);
        }

        public string Title
        {
            get => _title;
            private set => SetField(ref _title, value);
        }
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        /// <summary>
        /// 统一图像 DPI 的目标值（可调整）。
        /// </summary>
        public double TargetDpi
        {
            get => _targetDpi;
            set
            {
                if (SetField(ref _targetDpi, Math.Max(1, value)))
                {
                    SaveSettings();
                }
            }
        }

        public ObservableCollection<IcoResolution> AvailableResolutions { get; }
        public ICommand LoadImageCommand { get; }
        public ICommand ApplyCornerRadiusCommand { get; }
        public ICommand ConvertToIcoCommand { get; }
        /// <summary>
        /// 导出当前预览图像为 PNG 的命令。
        /// </summary>
        public ICommand ExportPreviewToPngCommand { get; }
        public ICommand BatchConvertCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ClearLogCommand { get; }
        private bool CanApplyCornerRadius() => !IsProcessing && _originalImage != null;

        private bool CanLoadImage(object? parameter) => !IsProcessing;

        /// <summary>
        /// 判断是否可以导出当前预览图像。
        /// </summary>
        private bool CanExportPreviewToPng(object? parameter) => !IsProcessing && PreviewImage != null;


        /// <summary>
        /// 切换主题并在设置中持久化用户偏好。
        /// </summary>
        private void ChangeTheme(object? arg)
        {
            var nextTheme = !_isDarkTheme;
            ApplyTheme(nextTheme);
            SaveSettings();
        }
        /// <summary>
        /// 将主题字典动态替换为浅色或深色版本。
        /// </summary>
        private void ApplyTheme(bool useDarkTheme)
        {
            var application = System.Windows.Application.Current;
            if (application == null)
            {
                _isDarkTheme = useDarkTheme;
                return;
            }

            var dictionaries = application.Resources.MergedDictionaries;
            var currentDictionary = dictionaries.FirstOrDefault(IsThemeDictionary);
            var nextUri = useDarkTheme ? _darkThemeUri : _lightThemeUri;
            var replacement = new ResourceDictionary { Source = nextUri };

            if (currentDictionary is null)
            {
                dictionaries.Add(replacement);
            }
            else
            {
                var index = dictionaries.IndexOf(currentDictionary);
                dictionaries.Insert(index, replacement);
                dictionaries.Remove(currentDictionary);
            }

            _isDarkTheme = useDarkTheme;
        }

        /// <summary>
        /// 判断资源字典是否属于主题目录，用于定位当前主题。
        /// </summary>
        private static bool IsThemeDictionary(ResourceDictionary dictionary)
        {
            var original = dictionary.Source?.OriginalString;
            if (string.IsNullOrEmpty(original))
            {
                return false;
            }

            return original.Contains("Styles/Theme/", StringComparison.OrdinalIgnoreCase);
        }
        /// <summary>
        /// 获取与当前ViewModel关联的窗口
        /// </summary>
        /// <returns>找到的窗口，如果未找到则返回null</returns>
        private Window? GetAssociatedWindow()
        {
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window.DataContext == this)
                {
                    return window;
                }
            }
            return null;
        }

        /// <summary>
        /// 窗口拖动
        /// </summary>
        private void DragWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var window = GetAssociatedWindow();
                window?.DragMove();
            });
        }

        /// <summary>
        /// 最小化窗口
        /// </summary>
        private void MinimizeWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var window = GetAssociatedWindow();
                if (window != null)
                {
                    window.WindowState = WindowState.Minimized;
                }
            });
        }

        /// <summary>
        /// 关闭窗口
        /// </summary>
        private void CloseWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var window = GetAssociatedWindow();
                window?.Close();
            });
        }

        /// <summary>
        /// 打开关于窗口。
        /// </summary>
        private void OpenAboutWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var owner = GetAssociatedWindow();
                var aboutWindow = new AboutWindow
                {
                    Owner = owner
                };
                aboutWindow.ShowDialog();
            });
        }
        private void LoadImage(object? parameter)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.svg;*.ico|所有文件|*.*",
                    Title = "选择图片"
                };

                if (dialog.ShowDialog() == true)
                {
                    ProcessImageFile(dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                AddLog($"加载图片时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 导出当前预览图像为 PNG 文件。
        /// </summary>
        private void ExportPreviewToPng(object? parameter)
        {
            if (PreviewImage == null)
            {
                return;
            }

            try
            {
                var baseName = string.IsNullOrWhiteSpace(ImagePath)
                    ? "preview"
                    : Path.GetFileNameWithoutExtension(ImagePath);

                var initialDirectory = string.IsNullOrWhiteSpace(ImagePath)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : Path.GetDirectoryName(ImagePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG 图片|*.png",
                    Title = "导出 PNG",
                    FileName = $"{baseName}.png",
                    InitialDirectory = initialDirectory,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() == true)
                {
                    using var stream = new System.IO.FileStream(dialog.FileName, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(PreviewImage));
                    encoder.Save(stream);

                    AddLog($"PNG 导出成功: {dialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"PNG 导出失败: {ex.Message}");
                CustomMessageBox.Show($"PNG 导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ApplyCornerRadiusAsync()
        {
            if (_originalImage == null) return;

            try
            {
                IsProcessing = true;
                var maxRadius = Math.Max(0, Math.Min(_originalImage.PixelWidth, _originalImage.PixelHeight) / 2);
                var radius = Math.Clamp(CornerRadius, 0, maxRadius);
                if (radius != CornerRadius)
                {
                    CornerRadius = radius;
                    AddLog($"圆角半径已调整为 {radius}px（受图片尺寸限制）。");
                }

                if (_lastProcessedSource == _originalImage &&
                    _lastProcessedImage != null &&
                    _lastProcessedRadius == radius &&
                    _lastProcessedQuality == SelectedCornerQuality)
                {
                    PreviewImage = _lastProcessedImage;
                    AddLog("圆角参数未变化，已复用缓存预览。");
                    return;
                }

                if (radius == 0)
                {
                    PreviewImage = _originalImage;
                    AddLog("圆角半径为 0，使用原图预览。");
                    _lastProcessedSource = _originalImage;
                    _lastProcessedImage = _originalImage;
                    _lastProcessedRadius = radius;
                    _lastProcessedQuality = SelectedCornerQuality;
                    return;
                }

                AddLog($"应用圆角半径: {radius}px, 质量: {SelectedCornerQuality}");

                // 在后台线程执行耗时图像处理，避免阻塞 UI
                BitmapSource imageToProcess = _originalImage;
                if (imageToProcess is System.Windows.Threading.DispatcherObject dobj && dobj.CheckAccess())
                {
                    if (imageToProcess.CanFreeze)
                        imageToProcess.Freeze();
                }

                var processedImage = await Task.Run(() => _imageProcessor.ApplyRoundedCorners(
                    imageToProcess,
                    radius,
                    SelectedCornerQuality));

                PreviewImage = processedImage;
                _lastProcessedSource = _originalImage;
                _lastProcessedImage = processedImage;
                _lastProcessedRadius = radius;
                _lastProcessedQuality = SelectedCornerQuality;
                AddLog("圆角已成功应用。");
            }
            catch (Exception ex)
            {
                AddLog($"应用圆角时出错: {ex.Message}");
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanConvertToIco() =>
            !IsProcessing && PreviewImage != null;

        private async Task ConvertToIcoAsync()
        {
            if (PreviewImage == null) return;

            try
            {
                IsProcessing = true;
                AddLog("开始转换到ICO格式...");

                var selectedResolutions = AvailableResolutions
                    .Where(r => r.IsSelected)
                    .Select(r => new System.Drawing.Size(r.Width, r.Height))
                    .ToList();

                if (selectedResolutions.Count == 0)
                {
                    AddLog("错误: 请至少选择一个分辨率。");
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "ICO 文件|*.ico",
                    DefaultExt = ".ico",
                    FileName = "icon.ico"
                };

                if (dialog.ShowDialog() == true)
                {
                    // 冻结副本以便跨线程安全使用
                    BitmapSource imageToConvert = PreviewImage;
                    if (imageToConvert is System.Windows.Freezable f && !f.IsFrozen)
                    {
                        try { f.Freeze(); } catch { }
                    }

                    var outPath = dialog.FileName;

                    _cts = new System.Threading.CancellationTokenSource();
                    try
                    {
                        await _icoConverterService.ConvertToIcoAsync(imageToConvert, outPath, selectedResolutions, _cts.Token);
                        AddLog($"ICO文件已保存: {outPath}");
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
                    AddLog($"转换到ICO时出错: {ex.Message}");
                }
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void CancelProcessing(object? parameter)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                try
                {
                    _cts.Cancel();
                    AddLog("已请求取消处理。");
                }
                catch { }
            }
        }

        private bool CanCancelProcessing(object? parameter)
        {
            return _cts != null && !_cts.IsCancellationRequested;
        }

        private void ClearLog(object? parameter)
        {
            _logService.Clear();
        }

        private bool CanBatchConvert() => !IsProcessing;

        private async Task BatchConvertAsync()
        {
            try
            {
                var openDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.svg|所有文件|*.*",
                    Title = "选择要批量转换的图片",
                    Multiselect = true
                };

                if (openDialog.ShowDialog() != true || openDialog.FileNames.Length == 0)
                {
                    return;
                }

                var selectedResolutions = AvailableResolutions
                    .Where(r => r.IsSelected)
                    .Select(r => new System.Drawing.Size(r.Width, r.Height))
                    .ToList();

                if (selectedResolutions.Count == 0)
                {
                    AddLog("错误: 请至少选择一个分辨率。");
                    return;
                }

                using var folderDialog = new WinForms.FolderBrowserDialog
                {
                    Description = "选择输出文件夹"
                };

                if (folderDialog.ShowDialog() != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
                {
                    return;
                }

                IsProcessing = true;
                _cts = new System.Threading.CancellationTokenSource();

                var result = await _batchConversionService.ConvertAsync(
                    openDialog.FileNames,
                    folderDialog.SelectedPath,
                    selectedResolutions,
                    CornerRadius,
                    SelectedCornerQuality,
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
                AddLog($"批量转换出错: {ex.Message}");
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

        public void ProcessImageFile(string filePath)
        {
            bool startedApply = false;
            try
            {
                if (IsIcoFile(filePath))
                {
                    OpenIcoToPngWindow(filePath);
                    return;
                }

                IsProcessing = true;
                AddLog($"处理图片: {filePath}");

                // 验证文件类型
                if (!_imageLoadService.IsSupportedImageFile(filePath))
                {
                    var extension = System.IO.Path.GetExtension(filePath).ToLower();
                    AddLog($"错误: 不支持的文件格式 '{extension}'。");
                    CustomMessageBox.Show($"不支持的文件格式 '{extension}'。请选择图片文件。",
                        "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 自动识别 SVG 或位图并加载
                var loadResult = _imageLoadService.LoadImage(filePath, TargetDpi, SvgRenderMaxSize);
                var normalizedBitmap = loadResult.Image;

                AddLog($"图片 DPI: {loadResult.OriginalDpiX:0.##} x {loadResult.OriginalDpiY:0.##}");

                _originalImage = normalizedBitmap;
                PreviewImage = normalizedBitmap;
                ImagePath = filePath;


                _lastProcessedSource = null;
                _lastProcessedImage = null;
                _lastProcessedRadius = -1;

                CommandManager.InvalidateRequerySuggested();

                AddLog($"图片已加载: {normalizedBitmap.PixelWidth}x{normalizedBitmap.PixelHeight}");

                // 自动应用默认圆角
                if (CornerRadius > 0)
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
                AddLog($"处理图片时出错: {ex.Message}");
                CustomMessageBox.Show($"无法加载图片: {ex.Message}",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (!startedApply)
                {
                    IsProcessing = false;
                }
            }
        }

        private void LoadSettings()
        {
            _settings = _settingsService.Load();
            _isDarkTheme = _settings.IsDarkTheme;
            _targetDpi = Math.Max(1, _settings.TargetDpi);
        }

        private void SaveSettings()
        {
            _settings.IsDarkTheme = _isDarkTheme;
            _settings.TargetDpi = _targetDpi;
            _settingsService.Save(_settings);
        }

        public void HandleFileDrop(string[] files)
        {
            if (files == null || files.Length == 0) return;

            var icoFile = files.FirstOrDefault(IsIcoFile);
            if (!string.IsNullOrEmpty(icoFile))
            {
                OpenIcoToPngWindow(icoFile);
                return;
            }

            // 过滤出支持的图片文件（含 SVG）
            var imageFiles = files.Where(_imageLoadService.IsSupportedImageFile).ToArray();

            if (imageFiles.Length > 0)
            {
                ProcessImageFile(imageFiles[0]);
            }
            else
            {
                AddLog("错误: 拖放的文件不是支持的图片格式。");
                CustomMessageBox.Show("拖放的文件不是支持的图片格式。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 判断是否为 ICO 文件。
        /// </summary>
        private static bool IsIcoFile(string filePath)
        {
            return string.Equals(System.IO.Path.GetExtension(filePath), ".ico", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 打开 ICO 转 PNG 的转换弹窗。
        /// </summary>
        private void OpenIcoToPngWindow(string icoPath)
        {
            // 使用异步调度，避免在拖放回调中阻塞 UI 线程导致拖放状态无法释放。
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var window = App.ServiceProvider.GetRequiredService<IcoToPngWindow>();
                window.Initialize(icoPath);
                window.Owner = GetAssociatedWindow();
                window.ShowDialog();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void AddLog(string message)
        {
            _logService.Add(message);
        }

        /// <summary>
        /// 接收日志服务的更新并刷新 UI。
        /// </summary>
        private void OnLogUpdated(string logText, string statusMessage)
        {
            UpdateLogState(logText, statusMessage);
        }

        /// <summary>
        /// 将日志与状态同步到视图属性。
        /// </summary>
        private void UpdateLogState(string logText, string statusMessage)
        {
            LogText = logText;
            StatusMessage = statusMessage;
        }

        private void UpdateStatusMessage()
        {
            if (IsProcessing)
            {
                StatusMessage = "处理中...";
            }
            else if (string.IsNullOrEmpty(ImagePath))
            {
                StatusMessage = "就绪 - 请加载图片";
            }
            else
            {
                StatusMessage = $"就绪 - 当前图片: {System.IO.Path.GetFileName(ImagePath)}";
            }
        }
    }

    public enum CornerQuality
    {
        Low,
        Medium,
        High
    }
}
