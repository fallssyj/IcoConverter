using IcoConverter.Models;
using IcoConverter.Services;
using IcoConverter.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

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
        private int _cornerRadiusMaximum = DefaultCornerRadiusMaximum;
        private string _logText = string.Empty;
        private bool _isProcessing = false;
        private System.Threading.CancellationTokenSource? _cts;
        private string _statusMessage = "就绪";
        private string _title = "IcoConverter - 图片转ICO工具";
        private AppSettings _settings = new();
        private BitmapSource? _lastProcessedSource;
        private BitmapSource? _lastProcessedImage;
        private int _lastProcessedRadius = -1;
        private MaskShape _selectedMaskShape = MaskShape.RoundedRectangle;
        private int _polygonSides = 6;
        private double _polygonRotation;
        private MaskShape _lastProcessedShape = MaskShape.RoundedRectangle;
        private int _lastProcessedPolygonSides = 6;
        private double _lastProcessedPolygonRotation = double.NaN;
        private double _lastImageScale = double.NaN;
        private double _lastImageOffsetX = double.NaN;
        private double _lastImageOffsetY = double.NaN;
        private double _imageScale = 1d;
        private double _imageOffsetX;
        private double _imageOffsetY;
        private double _imageOffsetLimit = DefaultImageOffsetLimit;
        private readonly DispatcherTimer _previewDebounceTimer;
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
        private bool _pendingPreviewUpdate;
        private const int DefaultCornerRadiusMaximum = 1024;
        private const int MinimumCornerRadiusMaximum = 32;
        private const int RecommendedCornerRadiusHint = 96;
        private const int AutoPreviewDisableThreshold = 2048;
        private const double MinImageScale = 0.1d;
        private const double MaxImageScale = 4d;
        private const double DefaultImageOffsetLimit = 2048d;
        // SVG 渲染的最大边长（像素），用于保证输出清晰度
        private const int SvgRenderMaxSize = 1024;
        // 统一图像 DPI 的目标值（可调整）
        private double _targetDpi = 96d;
        private bool _isRealTimePreviewEnabled = true;
        private bool _isMaskSettingsExpanded = true;
        private bool _isIcoResolutionExpanded = true;
        private bool _isLogExpanded;
        private readonly Stack<MaskStateSnapshot> _undoStack = new();
        private readonly Stack<MaskStateSnapshot> _redoStack = new();
        private bool _suspendUndoCapture;
        private bool _isRestoringState;
        private bool _suppressPreviewToggleLog;
        private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".dll" };
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
            UndoMaskCommand = new RelayCommand(_ => UndoMaskChanges(), _ => CanUndoMask);
            RedoMaskCommand = new RelayCommand(_ => RedoMaskChanges(), _ => CanRedoMask);
            IncreaseCornerRadiusCommand = new RelayCommand(_ => AdjustCornerRadius(5));
            DecreaseCornerRadiusCommand = new RelayCommand(_ => AdjustCornerRadius(-5));
            IncreasePolygonRotationCommand = new RelayCommand(_ => AdjustPolygonRotation(5));
            DecreasePolygonRotationCommand = new RelayCommand(_ => AdjustPolygonRotation(-5));
            ClearMaskHistoryCommand = new RelayCommand(_ => ResetHistory(), _ => MaskHistory.Count > 0);
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
                    ReportError("无法打开浏览器", ex);
                }
            });
            // 初始化可选分辨率列表
            AvailableResolutions = new ObservableCollection<IcoResolution>(
                DefaultIcoResolutions
                    .Select(size => new IcoResolution(size.Width, size.Height, true)));

            MaskShapeOptions = new ObservableCollection<MaskShape>();
            foreach (MaskShape shape in Enum.GetValues(typeof(MaskShape)))
            {
                MaskShapeOptions.Add(shape);
            }

            _previewDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _previewDebounceTimer.Tick += PreviewDebounceTimer_Tick;

            LoadSettings();
            ApplyTheme(_isDarkTheme);
            UpdateHighResolutionOptions(null);
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
                if (CornerRadiusMaximum > 0)
                {
                    safeValue = Math.Min(safeValue, CornerRadiusMaximum);
                }
                var snapshot = CreateMaskStateSnapshot();
                if (SetField(ref _cornerRadius, safeValue))
                {
                    RegisterUndoSnapshot(snapshot);
                    SchedulePreviewUpdate();
                }
            }
        }

        public int CornerRadiusMaximum
        {
            get => _cornerRadiusMaximum;
            private set => SetField(ref _cornerRadiusMaximum, Math.Max(0, value));
        }

        public ObservableCollection<MaskShape> MaskShapeOptions { get; }

        public MaskShape SelectedMaskShape
        {
            get => _selectedMaskShape;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                if (SetField(ref _selectedMaskShape, value))
                {
                    RegisterUndoSnapshot(snapshot);
                    OnPropertyChanged(nameof(IsCornerRadiusEnabled));
                    OnPropertyChanged(nameof(IsPolygonShapeSelected));
                    SchedulePreviewUpdate();
                }
            }
        }

        public bool IsCornerRadiusEnabled => SelectedMaskShape == MaskShape.RoundedRectangle
            || SelectedMaskShape == MaskShape.Circle
            || SelectedMaskShape == MaskShape.Polygon;

        public bool IsPolygonShapeSelected => SelectedMaskShape == MaskShape.Polygon;

        public double ImageScale
        {
            get => _imageScale;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var clamped = Math.Clamp(value, MinImageScale, MaxImageScale);
                if (SetField(ref _imageScale, clamped))
                {
                    RegisterUndoSnapshot(snapshot);
                    SaveSettings();
                    SchedulePreviewUpdate();
                }
            }
        }

        public double ImageOffsetX
        {
            get => _imageOffsetX;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var clamped = Math.Clamp(value, ImageOffsetMinimum, ImageOffsetMaximum);
                if (SetField(ref _imageOffsetX, clamped))
                {
                    RegisterUndoSnapshot(snapshot);
                    SaveSettings();
                    SchedulePreviewUpdate();
                }
            }
        }

        public double ImageOffsetY
        {
            get => _imageOffsetY;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var clamped = Math.Clamp(value, ImageOffsetMinimum, ImageOffsetMaximum);
                if (SetField(ref _imageOffsetY, clamped))
                {
                    RegisterUndoSnapshot(snapshot);
                    SaveSettings();
                    SchedulePreviewUpdate();
                }
            }
        }

        public double ImageOffsetLimit
        {
            get => _imageOffsetLimit;
            private set
            {
                if (SetField(ref _imageOffsetLimit, value))
                {
                    OnPropertyChanged(nameof(ImageOffsetMinimum));
                    OnPropertyChanged(nameof(ImageOffsetMaximum));
                }
            }
        }

        public double ImageOffsetMinimum => -ImageOffsetLimit;

        public double ImageOffsetMaximum => ImageOffsetLimit;

        public int PolygonSides
        {
            get => _polygonSides;
            set
            {
                var safeValue = Math.Clamp(value, 3, 64);
                var snapshot = CreateMaskStateSnapshot();
                if (SetField(ref _polygonSides, safeValue))
                {
                    RegisterUndoSnapshot(snapshot);
                    SchedulePreviewUpdate();
                }
            }
        }

        public double PolygonRotation
        {
            get => _polygonRotation;
            set
            {
                var safeValue = Math.Clamp(value, -180d, 180d);
                var snapshot = CreateMaskStateSnapshot();
                if (SetField(ref _polygonRotation, safeValue))
                {
                    RegisterUndoSnapshot(snapshot);
                    SchedulePreviewUpdate();
                }
            }
        }

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

        public bool IsRealTimePreviewEnabled
        {
            get => _isRealTimePreviewEnabled;
            set
            {
                if (SetField(ref _isRealTimePreviewEnabled, value))
                {
                    SaveSettings();
                    CommandManager.InvalidateRequerySuggested();
                    if (value)
                    {
                        SchedulePreviewUpdate();
                    }
                    else if (!_suppressPreviewToggleLog)
                    {
                        AddLog("已关闭实时预览，可在调整参数后手动点击 '应用蒙版'。");
                    }
                    _suppressPreviewToggleLog = false;
                }
            }
        }

        public bool IsMaskSettingsExpanded
        {
            get => _isMaskSettingsExpanded;
            set
            {
                if (SetField(ref _isMaskSettingsExpanded, value))
                {
                    SaveSettings();
                }
            }
        }

        public bool IsIcoResolutionExpanded
        {
            get => _isIcoResolutionExpanded;
            set
            {
                if (SetField(ref _isIcoResolutionExpanded, value))
                {
                    SaveSettings();
                }
            }
        }

        public bool IsLogExpanded
        {
            get => _isLogExpanded;
            set
            {
                if (SetField(ref _isLogExpanded, value))
                {
                    SaveSettings();
                }
            }
        }

        public ObservableCollection<IcoResolution> AvailableResolutions { get; }
        public ObservableCollection<string> MaskHistory { get; } = new();
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
        public ICommand UndoMaskCommand { get; }
        public ICommand RedoMaskCommand { get; }
        public ICommand IncreaseCornerRadiusCommand { get; }
        public ICommand DecreaseCornerRadiusCommand { get; }
        public ICommand IncreasePolygonRotationCommand { get; }
        public ICommand DecreasePolygonRotationCommand { get; }
        public ICommand ClearMaskHistoryCommand { get; }
        public bool CanUndoMask => _undoStack.Count > 0;
        public bool CanRedoMask => _redoStack.Count > 0;
        private bool CanApplyCornerRadius() => !IsProcessing && _originalImage != null;

        private bool CanLoadImage(object? parameter) => !IsProcessing;

        /// <summary>
        /// 判断是否可以导出当前预览图像。
        /// </summary>
        private bool CanExportPreviewToPng(object? parameter) => !IsProcessing && PreviewImage != null;

        private void SchedulePreviewUpdate()
        {
            if (_originalImage == null)
            {
                return;
            }

            if (!IsRealTimePreviewEnabled)
            {
                return;
            }

            _pendingPreviewUpdate = true;
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }

        private async void PreviewDebounceTimer_Tick(object? sender, EventArgs e)
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
                    Filter = "图片或可执行文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff;*.svg;*.ico;*.exe;*.dll|所有文件|*.*",
                    Title = "选择图片或可执行文件"
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
                ReportError("PNG 导出失败", ex);
            }
        }

        private async Task ApplyCornerRadiusAsync()
        {
            if (_originalImage == null) return;

            try
            {
                IsProcessing = true;
                var shape = SelectedMaskShape;
                var polygonSides = PolygonSides;
                var polygonRotation = PolygonRotation;
                var maxRadius = Math.Max(0, Math.Min(_originalImage.PixelWidth, _originalImage.PixelHeight) / 2);
                var radius = Math.Clamp(CornerRadius, 0, maxRadius);

                if ((shape == MaskShape.Circle || shape == MaskShape.Polygon) && radius == 0)
                {
                    radius = maxRadius;
                }

                if (shape == MaskShape.RoundedRectangle && radius != CornerRadius)
                {
                    CornerRadius = radius;
                    AddLog($"圆角半径已调整为 {radius}px（受图片尺寸限制）。");
                }

                var transformOptions = new ImageTransformOptions(ImageScale, ImageOffsetX, ImageOffsetY);

                if (_lastProcessedSource == _originalImage &&
                    _lastProcessedImage != null &&
                    _lastProcessedRadius == radius &&
                    _lastProcessedShape == shape &&
                    _lastProcessedPolygonSides == polygonSides &&
                    Math.Abs(_lastProcessedPolygonRotation - polygonRotation) < 0.0001 &&
                    Math.Abs(_lastImageScale - transformOptions.Scale) < 0.0001 &&
                    Math.Abs(_lastImageOffsetX - transformOptions.OffsetX) < 0.01 &&
                    Math.Abs(_lastImageOffsetY - transformOptions.OffsetY) < 0.01)
                {
                    PreviewImage = _lastProcessedImage;
                    AddLog("蒙版参数未变化，已复用缓存预览。");
                    return;
                }

                var requiresMask = shape switch
                {
                    MaskShape.RoundedRectangle => radius > 0,
                    MaskShape.Circle => radius > 0,
                    _ => true
                };

                var requiresProcessing = requiresMask || !transformOptions.IsIdentity;

                if (!requiresProcessing)
                {
                    PreviewImage = _originalImage;
                    AddLog("当前蒙版配置无需裁剪，已使用原图预览。");
                    _lastProcessedSource = _originalImage;
                    _lastProcessedImage = _originalImage;
                    _lastProcessedRadius = radius;
                    _lastProcessedShape = shape;
                    _lastProcessedPolygonSides = polygonSides;
                    _lastProcessedPolygonRotation = polygonRotation;
                    _lastImageScale = transformOptions.Scale;
                    _lastImageOffsetX = transformOptions.OffsetX;
                    _lastImageOffsetY = transformOptions.OffsetY;
                    return;
                }

                if (requiresMask && !transformOptions.IsIdentity)
                {
                    AddLog($"应用蒙版与图像变换: {shape}，半径 {radius}px，缩放 {transformOptions.Scale:0.##}x，偏移 ({transformOptions.OffsetX:0.#}, {transformOptions.OffsetY:0.#})px");
                }
                else if (requiresMask)
                {
                    AddLog($"应用蒙版: {shape}，半径 {radius}px");
                }
                else
                {
                    AddLog($"应用图像变换: 缩放 {transformOptions.Scale:0.##}x，偏移 ({transformOptions.OffsetX:0.#}, {transformOptions.OffsetY:0.#})px");
                }

                var stopwatch = Stopwatch.StartNew();

                // 在后台线程执行耗时图像处理，避免阻塞 UI
                BitmapSource imageToProcess = _originalImage;
                if (imageToProcess is DispatcherObject dobj && dobj.CheckAccess())
                {
                    if (imageToProcess.CanFreeze)
                        imageToProcess.Freeze();
                }

                var processedImage = await Task.Run(() => _imageProcessor.ApplyMask(
                    imageToProcess,
                    shape,
                    radius,
                    polygonSides,
                    polygonRotation,
                    transformOptions));

                stopwatch.Stop();

                PreviewImage = processedImage;
                _lastProcessedSource = _originalImage;
                _lastProcessedImage = processedImage;
                _lastProcessedRadius = radius;
                _lastProcessedShape = shape;
                _lastProcessedPolygonSides = polygonSides;
                _lastProcessedPolygonRotation = polygonRotation;
                _lastImageScale = transformOptions.Scale;
                _lastImageOffsetX = transformOptions.OffsetX;
                _lastImageOffsetY = transformOptions.OffsetY;
                AddLog($"蒙版应用完成，耗时 {stopwatch.ElapsedMilliseconds}ms，输出 {processedImage.PixelWidth}x{processedImage.PixelHeight}");
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

        private bool CanConvertToIco() =>
            !IsProcessing && PreviewImage != null;

        private async Task ConvertToIcoAsync()
        {
            if (PreviewImage == null) return;

            try
            {
                IsProcessing = true;
                AddLog("开始转换到ICO格式...");

                if (!TryBuildSelectedResolutions(out var selectedResolutions))
                {
                    return;
                }

                // 让用户选择 ICO 输出路径，默认文件名为 icon.ico。
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
                    ReportError("转换到ICO时出错", ex);
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
                // 批量模式一次性选择多张图片，再统一转换。
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

                if (!TryBuildSelectedResolutions(out var selectedResolutions))
                {
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
                    SelectedMaskShape,
                    PolygonSides,
                    PolygonRotation,
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

        /// <summary>
        /// 汇总当前勾选的 ICO 分辨率，若未选择则提示用户。
        /// </summary>
        private bool TryBuildSelectedResolutions(out List<System.Drawing.Size> selectedResolutions)
        {
            selectedResolutions = AvailableResolutions
                .Where(r => r.IsSelected)
                .Select(r => new System.Drawing.Size(r.Width, r.Height))
                .Distinct()
                .OrderBy(size => size.Width)
                .ToList();

            if (selectedResolutions.Count > 0)
            {
                return true;
            }

            ReportWarning("请至少选择一个分辨率。");
            return false;
        }

        public void ProcessImageFile(string filePath)
        {
            // 标记是否已经启动异步蒙版应用，用于 finally 中正确恢复 IsProcessing。
            bool startedApply = false;
            try
            {
                filePath = ShortcutResolver.ResolveShortcutTarget(filePath);

                if (IsExecutableFile(filePath))
                {
                    OpenExecutableIconPicker(filePath);
                    return;
                }

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
                    ReportWarning($"不支持的文件格式 '{extension}'。请选择图片文件。");
                    return;
                }

                // 自动识别 SVG 或位图并加载
                var loadResult = _imageLoadService.LoadImage(filePath, TargetDpi, SvgRenderMaxSize);
                var normalizedBitmap = loadResult.Image;

                AddLog($"图片 DPI: {loadResult.OriginalDpiX:0.##} x {loadResult.OriginalDpiY:0.##}");
                AddLog($"加载 DPI: {normalizedBitmap.DpiX:0.##} x {normalizedBitmap.DpiY:0.##}");

                _originalImage = normalizedBitmap;
                PreviewImage = normalizedBitmap;
                ImagePath = filePath;
                UpdateCornerRadiusMaximum(normalizedBitmap);
                UpdateHighResolutionOptions(normalizedBitmap);
                ResetImageTransform();
                UpdateImageTransformLimits(normalizedBitmap);


                _lastProcessedSource = null;
                _lastProcessedImage = null;
                _lastProcessedRadius = -1;
                _lastProcessedShape = MaskShape.RoundedRectangle;
                _lastProcessedPolygonSides = -1;
                _lastProcessedPolygonRotation = double.NaN;
                _lastImageScale = double.NaN;
                _lastImageOffsetX = double.NaN;
                _lastImageOffsetY = double.NaN;
                _pendingPreviewUpdate = false;
                _previewDebounceTimer.Stop();
                ResetHistory();

                CommandManager.InvalidateRequerySuggested();

                AddLog($"图片已加载: {normalizedBitmap.PixelWidth}x{normalizedBitmap.PixelHeight}");

                var largestDimension = Math.Max(normalizedBitmap.PixelWidth, normalizedBitmap.PixelHeight);
                if (largestDimension >= AutoPreviewDisableThreshold && IsRealTimePreviewEnabled)
                {
                    _suppressPreviewToggleLog = true;
                    IsRealTimePreviewEnabled = false;
                    AddLog($"图片较大（最长边 {largestDimension}px），已自动关闭实时预览。");
                }

                var currentTransform = new ImageTransformOptions(ImageScale, ImageOffsetX, ImageOffsetY);
                bool shouldAutoApply = !currentTransform.IsIdentity || SelectedMaskShape switch
                {
                    MaskShape.RoundedRectangle => CornerRadius > 0,
                    MaskShape.Circle => true,
                    _ => true
                };

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
                ReportError("处理图片时出错", ex);
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
            _isRealTimePreviewEnabled = _settings.IsRealTimePreviewEnabled;
            _isMaskSettingsExpanded = _settings.IsMaskSettingsExpanded;
            _isIcoResolutionExpanded = _settings.IsIcoResolutionExpanded;
            _isLogExpanded = _settings.IsLogExpanded;
            _cornerRadius = Math.Max(0, _settings.LastCornerRadius);
            _polygonSides = Math.Clamp(_settings.LastPolygonSides, 3, 64);
            _polygonRotation = Math.Clamp(_settings.LastPolygonRotation, -180d, 180d);
            _imageScale = Math.Clamp(_settings.LastImageScale <= 0 ? 1d : _settings.LastImageScale, MinImageScale, MaxImageScale);
            _imageOffsetX = _settings.LastImageOffsetX;
            _imageOffsetY = _settings.LastImageOffsetY;

            OnPropertyChanged(nameof(ImageScale));
            OnPropertyChanged(nameof(ImageOffsetX));
            OnPropertyChanged(nameof(ImageOffsetY));
        }

        private void SaveSettings()
        {
            _settings.IsDarkTheme = _isDarkTheme;
            _settings.TargetDpi = _targetDpi;
            _settings.IsRealTimePreviewEnabled = _isRealTimePreviewEnabled;
            _settings.IsMaskSettingsExpanded = _isMaskSettingsExpanded;
            _settings.IsIcoResolutionExpanded = _isIcoResolutionExpanded;
            _settings.IsLogExpanded = _isLogExpanded;
            _settings.LastCornerRadius = _cornerRadius;
            _settings.LastPolygonSides = _polygonSides;
            _settings.LastPolygonRotation = _polygonRotation;
            _settings.LastImageScale = _imageScale;
            _settings.LastImageOffsetX = _imageOffsetX;
            _settings.LastImageOffsetY = _imageOffsetY;
            _settingsService.Save(_settings);
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

            var icoFile = resolvedFiles.FirstOrDefault(IsIcoFile);
            if (!string.IsNullOrEmpty(icoFile))
            {
                OpenIcoToPngWindow(icoFile);
                return;
            }

            // 过滤出支持的图片文件（含 SVG）
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

        /// <summary>
        /// 判断是否为 ICO 文件。
        /// </summary>
        private static bool IsIcoFile(string filePath)
        {
            return string.Equals(System.IO.Path.GetExtension(filePath), ".ico", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断是否为可执行文件。
        /// </summary>
        private static bool IsExecutableFile(string filePath)
        {
            var extension = System.IO.Path.GetExtension(filePath);
            return !string.IsNullOrWhiteSpace(extension) && ExecutableExtensions.Contains(extension);
        }

        /// <summary>
        /// 打开图标提取窗口，供用户选择并导出可执行文件中的图标。
        /// </summary>
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

        private void AdjustCornerRadius(int delta)
        {
            var next = CornerRadius + delta;
            CornerRadius = Math.Clamp(next, 0, CornerRadiusMaximum);
        }

        private void AdjustPolygonRotation(double delta)
        {
            var next = PolygonRotation + delta;
            PolygonRotation = Math.Clamp(next, -180d, 180d);
        }

        private void UpdateHighResolutionOptions(BitmapSource? source)
        {
            var minSide = source == null ? 0 : Math.Min(source.PixelWidth, source.PixelHeight);

            foreach (var size in OptionalPngResolutions)
            {
                var shouldShow = minSide >= size.Width;
                var existing = AvailableResolutions.FirstOrDefault(r => r.Width == size.Width && r.Height == size.Height);

                if (shouldShow && existing == null)
                {
                    var insertIndex = GetResolutionInsertIndex(size.Width, size.Height);
                    AvailableResolutions.Insert(insertIndex, new IcoResolution(size.Width, size.Height, true));
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

            if (CornerRadius > CornerRadiusMaximum)
            {
                _suspendUndoCapture = true;
                CornerRadius = CornerRadiusMaximum;
                _suspendUndoCapture = false;
            }

            if (CornerRadiusMaximum != previousMax)
            {
                var recommended = Math.Min(CornerRadiusMaximum, RecommendedCornerRadiusHint);
                AddLog($"圆角上限已自动调整为 {CornerRadiusMaximum}px，推荐值不超过 {recommended}px 以兼顾边角细节。");
            }
        }

        private void UpdateImageTransformLimits(BitmapSource? source)
        {
            double limit = DefaultImageOffsetLimit;

            if (source != null)
            {
                limit = Math.Max(source.PixelWidth, source.PixelHeight);
            }

            ImageOffsetLimit = limit;
            ClampImageOffsets();
        }

        private void ClampImageOffsets()
        {
            var clampedX = Math.Clamp(ImageOffsetX, ImageOffsetMinimum, ImageOffsetMaximum);
            var clampedY = Math.Clamp(ImageOffsetY, ImageOffsetMinimum, ImageOffsetMaximum);

            if (Math.Abs(clampedX - ImageOffsetX) > 0.0001)
            {
                _suspendUndoCapture = true;
                ImageOffsetX = clampedX;
                _suspendUndoCapture = false;
            }

            if (Math.Abs(clampedY - ImageOffsetY) > 0.0001)
            {
                _suspendUndoCapture = true;
                ImageOffsetY = clampedY;
                _suspendUndoCapture = false;
            }
        }

        private void ResetImageTransform()
        {
            _suspendUndoCapture = true;
            ImageScale = 1d;
            ImageOffsetX = 0d;
            ImageOffsetY = 0d;
            _suspendUndoCapture = false;
        }

        private MaskStateSnapshot CreateMaskStateSnapshot() => new(
            CornerRadius,
            SelectedMaskShape,
            PolygonSides,
            Math.Round(PolygonRotation, 3),
            Math.Round(ImageScale, 4),
            Math.Round(ImageOffsetX, 2),
            Math.Round(ImageOffsetY, 2));

        private void RegisterUndoSnapshot(MaskStateSnapshot snapshot)
        {
            if (_suspendUndoCapture || _isRestoringState)
            {
                return;
            }

            if (_undoStack.TryPeek(out var top) && top.Equals(snapshot))
            {
                return;
            }

            _undoStack.Push(snapshot);
            _redoStack.Clear();
            CommandManager.InvalidateRequerySuggested();
        }

        private void RestoreMaskState(MaskStateSnapshot snapshot, string logPrefix)
        {
            _suspendUndoCapture = true;
            _isRestoringState = true;
            try
            {
                CornerRadius = snapshot.CornerRadius;
                SelectedMaskShape = snapshot.Shape;
                PolygonSides = snapshot.PolygonSides;
                PolygonRotation = snapshot.PolygonRotation;
                ImageScale = snapshot.ImageScale;
                ImageOffsetX = snapshot.ImageOffsetX;
                ImageOffsetY = snapshot.ImageOffsetY;
            }
            finally
            {
                _isRestoringState = false;
                _suspendUndoCapture = false;
            }

            AddLog($"{logPrefix}: {DescribeMaskState(snapshot)}");
            AddHistoryEntry($"{logPrefix} - {DescribeMaskState(snapshot)}");
            SchedulePreviewUpdate();
            CommandManager.InvalidateRequerySuggested();
        }

        private void UndoMaskChanges()
        {
            if (_undoStack.Count == 0)
            {
                return;
            }

            var current = CreateMaskStateSnapshot();
            var snapshot = _undoStack.Pop();
            _redoStack.Push(current);
            RestoreMaskState(snapshot, "已撤销到");
        }

        private void RedoMaskChanges()
        {
            if (_redoStack.Count == 0)
            {
                return;
            }

            var current = CreateMaskStateSnapshot();
            var snapshot = _redoStack.Pop();
            _undoStack.Push(current);
            RestoreMaskState(snapshot, "已重做到");
        }

        private string DescribeMaskState(MaskStateSnapshot snapshot)
            => $"{snapshot.Shape} · R={snapshot.CornerRadius}px · 边={snapshot.PolygonSides} · 旋转={snapshot.PolygonRotation:0.#}° · 缩放={snapshot.ImageScale:0.##}x · X={snapshot.ImageOffsetX:0.#}px · Y={snapshot.ImageOffsetY:0.#}px";

        private void AddHistoryEntry(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            MaskHistory.Insert(0, message);
            const int historyLimit = 20;
            while (MaskHistory.Count > historyLimit)
            {
                MaskHistory.RemoveAt(MaskHistory.Count - 1);
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void ResetHistory()
        {
            MaskHistory.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            CommandManager.InvalidateRequerySuggested();
        }

        private void AddLog(string message)
        {
            _logService.Add(message);
        }

        private void ReportError(string message, Exception? exception = null)
        {
            var finalMessage = exception == null ? message : $"{message} ({exception.Message})";
            AddLog($"错误: {finalMessage}");
            CustomMessageBox.Show(finalMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ReportWarning(string message)
        {
            AddLog($"警告: {message}");
            CustomMessageBox.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ReportInfo(string message)
        {
            AddLog(message);
            CustomMessageBox.Show(message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private readonly record struct MaskStateSnapshot(
            int CornerRadius,
            MaskShape Shape,
            int PolygonSides,
            double PolygonRotation,
            double ImageScale,
            double ImageOffsetX,
            double ImageOffsetY);

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
}
