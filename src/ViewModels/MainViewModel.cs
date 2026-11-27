using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;
using IcoConverter.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;

namespace IcoConverter.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private bool _isDarkTheme;

        private WindowMaterial _material = WindowMaterial.Mica;

        private readonly IImageProcessor _imageProcessor;
        private readonly ILogService _logService;
        private readonly ISettingsService _settingsService;

        private PreviewBackgroundMode _previewBackgroundMode = PreviewBackgroundMode.Checker;
        private string _logText = string.Empty;
        private bool _isProcessing = false;
        private string _statusMessage = "就绪";
        private string _notificationTitle = "精简模式";
        private string _notificationMessage = "仅保留圆角与圆形蒙版，支持拖放图片/SVG/EXE/DLL，路径更清晰。";
        private NotificationLevel _notificationSeverity = NotificationLevel.Informational;
        private string _title = "IcoConverter - 图片转图标工具";
        private AppSettings _settings = new();
        private readonly DispatcherTimer _settingsSaveDebounceTimer;
        private bool _hasPendingSettingsSave;
        private const int SettingsSaveDebounceMilliseconds = 600;
        private double _targetDpi = 96d;
        private bool _isRealTimePreviewEnabled = true;
        private bool _isMaskSettingsExpanded = true;
        private bool _isIcoResolutionExpanded = true;
        private bool _isLogExpanded;
        private bool _suppressPreviewToggleLog;
        public IRelayCommand ChangeThemeCommand { get; }
        public IRelayCommand OpenAboutCommand { get; }
        public IRelayCommand<PreviewBackgroundMode> SelectPreviewBackgroundCommand { get; }

        public MainViewModel(
            IImageProcessor imageProcessor,
            IIcoConverterService icoConverterService,
            IIcnsConverterService icnsConverterService,
            ILogService logService,
            ISettingsService settingsService,
            IImageLoadService imageLoadService,
            IBatchConversionService batchConversionService,
            WebsiteIconExportService websiteIconExportService)
        {
            _imageProcessor = imageProcessor;
            _icoConverterService = icoConverterService;
            _icnsConverterService = icnsConverterService;
            _logService = logService;
            _settingsService = settingsService;
            _imageLoadService = imageLoadService;
            _batchConversionService = batchConversionService;
            _websiteIconExportService = websiteIconExportService;
            _logService.LogUpdated += OnLogUpdated;

            LoadImageCommand = new RelayCommand(LoadImage, CanLoadImage);
            ConvertToIcoCommand = new AsyncRelayCommand(ConvertToIcoAsync, CanConvertToIco);
            ConvertToIcnsCommand = new AsyncRelayCommand(ConvertToIcnsAsync, CanConvertToIco);
            ExportPreviewPngCommand = new AsyncRelayCommand(
                () => ExportPreviewAsync(PreviewExportFormat.Png),
                CanExportPreview);
            ExportPreviewWebpCommand = new AsyncRelayCommand(
                () => ExportPreviewAsync(PreviewExportFormat.Webp),
                CanExportPreview);
            BatchConvertCommand = new AsyncRelayCommand(BatchConvertAsync, CanBatchConvert);
            CancelCommand = new RelayCommand(CancelProcessing, CanCancelProcessing);
            ChangeThemeCommand = new RelayCommand(ChangeTheme);
            OpenAboutCommand = new RelayCommand(OpenAboutWindow);
            WindowDragOverCommand = new RelayCommand<System.Windows.DragEventArgs>(HandleWindowDragOver);
            WindowDropCommand = new RelayCommand<System.Windows.DragEventArgs>(HandleWindowDrop);
            WindowDragLeaveCommand = new RelayCommand<System.Windows.DragEventArgs>(HandleWindowDragLeave);
            ApplyRadiusPresetCommand = new RelayCommand<int>(ApplyRadiusPreset);
            SelectMaskShapeCommand = new RelayCommand<MaskShape>(shape => SelectedMaskShape = shape);
            ResetMaskCommand = new RelayCommand(ResetMaskParameters);
            PickCanvasCustomColorCommand = new RelayCommand(PickCanvasCustomColor);
            ToggleCropModeCommand = new RelayCommand(ToggleCropMode, CanEditCrop);
            ApplyCropCommand = new AsyncRelayCommand(ApplyCropAsync, CanApplyCrop);
            CancelCropCommand = new RelayCommand(CancelCrop, CanCancelCrop);
            ResetCropCommand = new AsyncRelayCommand(ResetCropAsync, CanResetCrop);
            UndoCommand = new RelayCommand(Undo, CanUndo);
            RedoCommand = new RelayCommand(Redo, CanRedo);
            SelectPreviewBackgroundCommand = new RelayCommand<PreviewBackgroundMode>(mode => PreviewBackground = mode);
            AutoFitContentCommand = new AsyncRelayCommand(AutoFitContentAsync, CanEditSource);
            ClearSourceOverrideCommand = new RelayCommand(ClearSourceOverride, CanClearSourceOverride);
            ExportPngSetCommand = new AsyncRelayCommand(ExportPngSetAsync, CanExportPreview);
            ExportWebsiteIconsCommand = new AsyncRelayCommand(ExportWebsiteIconsAsync, CanExportPreview);
            AvailableResolutions = new ObservableCollection<IcoResolution>(
                DefaultIcoResolutions
                    .Select(size => new IcoResolution(size.Width, size.Height, true)));

            RadiusUnitOptions =
            [
                RadiusUnit.Pixels,
                RadiusUnit.Percent
            ];

            CircleFitModeOptions =
            [
                CircleFitMode.Inscribed,
                CircleFitMode.Circumscribed,
                CircleFitMode.Padded
            ];

            CanvasFillModeOptions =
            [
                CanvasFillMode.Transparent,
                CanvasFillMode.White,
                CanvasFillMode.Black,
                CanvasFillMode.Custom
            ];

            SupersampleModeOptions =
            [
                SupersampleMode.Off,
                SupersampleMode.Preview,
                SupersampleMode.Export,
                SupersampleMode.Both
            ];

            RadiusPresets = new ObservableCollection<RadiusPresetOption>();

            _previewDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _previewDebounceTimer.Tick += PreviewDebounceTimer_Tick;

            _settingsSaveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(SettingsSaveDebounceMilliseconds)
            };
            _settingsSaveDebounceTimer.Tick += SettingsSaveDebounceTimer_Tick;

            LoadSettings();
            ApplyTheme(_isDarkTheme);
            UpdateHighResolutionOptions(null);
            RefreshRadiusPresets();
            NotifyCommandStateChanges();

            UpdateLogState(_logService.LogText, _logService.StatusMessage);

            AddLog("IcoConverter 已启动，等待载入图像。");
        }

        public PreviewBackgroundMode PreviewBackground
        {
            get => _previewBackgroundMode;
            set
            {
                if (SetProperty(ref _previewBackgroundMode, value))
                {
                    OnPropertyChanged(nameof(PreviewBackgroundBrush));
                    SaveSettings();
                }
            }
        }

        public Brush PreviewBackgroundBrush => _previewBackgroundMode switch
        {
            PreviewBackgroundMode.White => Brushes.White,
            PreviewBackgroundMode.Black => Brushes.Black,
            PreviewBackgroundMode.None => Brushes.Transparent,
            _ => _checkerBrush ??= CreateCheckerBrush()
        };

        private Brush? _checkerBrush;

        private Brush CreateCheckerBrush()
        {
            // 深色主题下棋盘格不能沿用白灰色，否则预览区整体过亮
            var light = _isDarkTheme ? MediaColor.FromRgb(0x33, 0x33, 0x33) : MediaColor.FromRgb(0xFF, 0xFF, 0xFF);
            var dark = _isDarkTheme ? MediaColor.FromRgb(0x24, 0x24, 0x24) : MediaColor.FromRgb(0xE4, 0xE4, 0xE4);
            const double cell = 8d;

            var group = new DrawingGroup();
            using (var context = group.Open())
            {
                context.DrawRectangle(new SolidColorBrush(light), null, new Rect(0, 0, cell * 2, cell * 2));
                var darkBrush = new SolidColorBrush(dark);
                context.DrawRectangle(darkBrush, null, new Rect(0, 0, cell, cell));
                context.DrawRectangle(darkBrush, null, new Rect(cell, cell, cell, cell));
            }

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, cell * 2, cell * 2),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, cell * 2, cell * 2),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    NotifyCommandStateChanges();
                    UpdateStatusMessage();
                }
            }
        }

        public string Title
        {
            get => _title;
            private set => SetProperty(ref _title, value);
        }
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool IsDarkTheme => _isDarkTheme;

        public string ThemeToggleToolTip => _isDarkTheme ? "切换到浅色主题" : "切换到深色主题";

        public WindowMaterial Material => _material;

        public bool IsMicaMaterial => _material == WindowMaterial.Mica;

        public bool IsAcrylicMaterial => _material == WindowMaterial.Acrylic;

        public bool IsSolidMaterial => _material == WindowMaterial.Solid;

        /// <summary>切换已打开窗口的材质，并安排保存设置。</summary>
        public void SetMaterial(WindowMaterial material)
        {
            if (_material == material)
            {
                return;
            }

            _material = material;
            WindowMaterialService.ApplyToAll(material);

            OnPropertyChanged(nameof(Material));
            OnPropertyChanged(nameof(IsMicaMaterial));
            OnPropertyChanged(nameof(IsAcrylicMaterial));
            OnPropertyChanged(nameof(IsSolidMaterial));

            UpdateNotification("背景材质", $"已切换为{WindowMaterialService.GetDisplayName(material)}", NotificationLevel.Informational);
            SaveSettings();
        }

        public string NotificationTitle
        {
            get => _notificationTitle;
            private set => SetProperty(ref _notificationTitle, value);
        }

        public string NotificationMessage
        {
            get => _notificationMessage;
            private set => SetProperty(ref _notificationMessage, value);
        }

        public NotificationLevel NotificationSeverity
        {
            get => _notificationSeverity;
            private set => SetProperty(ref _notificationSeverity, value);
        }

        public double TargetDpi
        {
            get => _targetDpi;
            set
            {
                if (SetProperty(ref _targetDpi, Math.Max(1, value)))
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
                if (SetProperty(ref _isRealTimePreviewEnabled, value))
                {
                    SaveSettings();
                    NotifyCommandStateChanges();
                    if (value)
                    {
                        SchedulePreviewUpdate();
                    }
                    else if (!_suppressPreviewToggleLog)
                    {
                        AddLog("已关闭实时预览标记；参数变更后仍会自动更新预览。");
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
                if (SetProperty(ref _isMaskSettingsExpanded, value))
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
                if (SetProperty(ref _isIcoResolutionExpanded, value))
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
                if (SetProperty(ref _isLogExpanded, value))
                {
                    SaveSettings();
                }
            }
        }

        private void NotifyCommandStateChanges()
        {
            LoadImageCommand.NotifyCanExecuteChanged();
            ConvertToIcoCommand.NotifyCanExecuteChanged();
            ConvertToIcnsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanExportIcon));
            ExportPreviewPngCommand.NotifyCanExecuteChanged();
            ExportPreviewWebpCommand.NotifyCanExecuteChanged();
            BatchConvertCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
            ToggleCropModeCommand.NotifyCanExecuteChanged();
            ApplyCropCommand.NotifyCanExecuteChanged();
            CancelCropCommand.NotifyCanExecuteChanged();
            ResetCropCommand.NotifyCanExecuteChanged();
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
            AutoFitContentCommand.NotifyCanExecuteChanged();
            ClearSourceOverrideCommand.NotifyCanExecuteChanged();
            ExportPngSetCommand.NotifyCanExecuteChanged();
            ExportWebsiteIconsCommand.NotifyCanExecuteChanged();
        }

        private void SettingsSaveDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _settingsSaveDebounceTimer.Stop();
            SaveSettingsToDisk();
        }

        private void ChangeTheme()
        {
            ApplyTheme(!_isDarkTheme);
            SaveSettings();
        }

        private void ApplyTheme(bool useDarkTheme)
        {
            try
            {
                AppThemeService.Apply(useDarkTheme);
                _isDarkTheme = useDarkTheme;

                // 棋盘格的两种灰度依赖主题，切换主题后需重新生成
                _checkerBrush = null;
                OnPropertyChanged(nameof(PreviewBackgroundBrush));

                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(ThemeToggleToolTip));
                UpdateNotification("主题", useDarkTheme ? "已切换到深色主题" : "已切换到浅色主题", NotificationLevel.Informational);
            }
            catch (Exception ex)
            {
                ReportError("主题切换失败", ex);
            }
        }

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

        private void OpenAboutWindow()
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var owner = GetAssociatedWindow();
                var aboutWindow = App.ServiceProvider.GetRequiredService<AboutWindow>();
                aboutWindow.Owner = owner;
                aboutWindow.ShowDialog();
            });
        }
        private void LoadSettings()
        {
            _settings = _settingsService.Load();

            // 恢复应用偏好。蒙版、画布和变换参数从字段初始值开始。
            _isDarkTheme = _settings.IsDarkTheme;
            _material = _settings.Material;

            // 先设置当前材质，供主窗口的 Loaded 处理程序读取。
            WindowMaterialService.SetCurrent(_material);
            _targetDpi = Math.Max(1, _settings.TargetDpi);
            _isRealTimePreviewEnabled = _settings.IsRealTimePreviewEnabled;
            _isMaskSettingsExpanded = _settings.IsMaskSettingsExpanded;
            _isIcoResolutionExpanded = _settings.IsIcoResolutionExpanded;
            _isLogExpanded = _settings.IsLogExpanded;
            _previewBackgroundMode = _settings.PreviewBackground;
            _icoPngFrameMode = _settings.IcoPngFrameMode;
            _trimPaddingPercent = Math.Clamp(_settings.TrimPaddingPercent, 0, 45);
        }

        private void SaveSettings()
        {
            _settings.IsDarkTheme = _isDarkTheme;
            _settings.Material = _material;
            _settings.TargetDpi = _targetDpi;
            _settings.IsRealTimePreviewEnabled = _isRealTimePreviewEnabled;
            _settings.IsMaskSettingsExpanded = _isMaskSettingsExpanded;
            _settings.IsIcoResolutionExpanded = _isIcoResolutionExpanded;
            _settings.IsLogExpanded = _isLogExpanded;
            _settings.PreviewBackground = _previewBackgroundMode;
            _settings.IcoPngFrameMode = _icoPngFrameMode;
            _settings.TrimPaddingPercent = _trimPaddingPercent;

            _hasPendingSettingsSave = true;
            _settingsSaveDebounceTimer.Stop();
            _settingsSaveDebounceTimer.Start();
        }

        private void SaveSettingsToDisk()
        {
            if (!_hasPendingSettingsSave)
            {
                return;
            }

            _settingsService.Save(_settings);
            _hasPendingSettingsSave = false;
        }

        public void FlushPendingSettings()
        {
            _settingsSaveDebounceTimer.Stop();
            SaveSettingsToDisk();
        }

        private void AddLog(string message)
        {
            _logService.Add(message);
        }

        private void ReportError(string message, Exception? exception = null)
        {
            var finalMessage = exception == null ? message : $"{message} ({exception.Message})";
            AddLog($"错误: {finalMessage}");
            UpdateNotification("错误", finalMessage, NotificationLevel.Error);
            AppMessageBox.Show(finalMessage, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ReportWarning(string message)
        {
            AddLog($"警告: {message}");
            UpdateNotification("警告", message, NotificationLevel.Warning);
            AppMessageBox.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void OnLogUpdated(string logText, string statusMessage)
        {
            UpdateLogState(logText, statusMessage);
        }

        private void UpdateLogState(string logText, string statusMessage)
        {
            LogText = logText;
            StatusMessage = statusMessage;
            UpdateNotificationFromStatus(statusMessage);
        }

        private void UpdateStatusMessage()
        {
            if (IsProcessing)
            {
                StatusMessage = "处理中...";
                UpdateNotification("处理中", "正在执行当前操作，请稍候。", NotificationLevel.Informational);
            }
            else if (string.IsNullOrEmpty(ImagePath))
            {
                StatusMessage = "就绪 - 请加载图片";
                UpdateNotification("就绪", "请加载图片后再调整圆角或圆形蒙版。", NotificationLevel.Informational);
            }
            else
            {
                StatusMessage = $"就绪 - 当前图片: {System.IO.Path.GetFileName(ImagePath)}";
                UpdateNotification("就绪", StatusMessage, NotificationLevel.Informational);
            }
        }

        private void UpdateNotificationFromStatus(string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(statusMessage))
            {
                return;
            }

            var severity = ResolveSeverity(statusMessage);
            var title = severity switch
            {
                NotificationLevel.Error => "错误",
                NotificationLevel.Warning => "警告",
                NotificationLevel.Success => "成功",
                _ => IsProcessing ? "处理中" : "提示"
            };

            UpdateNotification(title, statusMessage, severity);
        }

        private static NotificationLevel ResolveSeverity(string statusMessage)
        {
            if (statusMessage.Contains("错误", StringComparison.OrdinalIgnoreCase)
                || statusMessage.Contains("失败", StringComparison.OrdinalIgnoreCase)
                || statusMessage.Contains("出错", StringComparison.OrdinalIgnoreCase))
            {
                return NotificationLevel.Error;
            }

            if (statusMessage.Contains("警告", StringComparison.OrdinalIgnoreCase)
                || statusMessage.Contains("不支持", StringComparison.OrdinalIgnoreCase))
            {
                return NotificationLevel.Warning;
            }

            if (statusMessage.Contains("成功", StringComparison.OrdinalIgnoreCase)
                || statusMessage.Contains("完成", StringComparison.OrdinalIgnoreCase)
                || statusMessage.Contains("已保存", StringComparison.OrdinalIgnoreCase))
            {
                return NotificationLevel.Success;
            }

            return NotificationLevel.Informational;
        }

        private void UpdateNotification(string title, string message, NotificationLevel severity)
        {
            NotificationTitle = title;
            NotificationMessage = message;
            NotificationSeverity = severity;
        }
    }
}

