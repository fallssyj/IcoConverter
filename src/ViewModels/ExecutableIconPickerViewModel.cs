using IcoConverter.Services;
using IcoConverter.Utils;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinForms = System.Windows.Forms;

namespace IcoConverter.ViewModels
{
    /// <summary>
    /// 提供可执行文件图标扫描与导出逻辑的视图模型。
    /// </summary>
    public class ExecutableIconPickerViewModel : ViewModelBase
    {
        private readonly IIcoConverterService _icoConverterService;
        private readonly ObservableCollection<ExecutableIconOption> _iconOptions = new();
        private readonly ReadOnlyObservableCollection<ExecutableIconOption> _readOnlyOptions;
        private string _binaryPath = string.Empty;
        private string _outputDirectory = string.Empty;
        private string _statusMessage = "等待扫描可执行文件";
        private bool _isLoading;
        private bool _isExporting;
        private CancellationTokenSource? _loadCts;

        public ExecutableIconPickerViewModel(IIcoConverterService icoConverterService)
        {
            _icoConverterService = icoConverterService;

            _readOnlyOptions = new ReadOnlyObservableCollection<ExecutableIconOption>(_iconOptions);

            _iconOptions.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(IconOptions));
                OnPropertyChanged(nameof(HasIcons));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                CommandManager.InvalidateRequerySuggested();
            };

            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput(), _ => !IsBusy);
            ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());
            SelectAllCommand = new RelayCommand(_ => SelectAll(), _ => !IsBusy && _iconOptions.Count > 0);
            ClearAllCommand = new RelayCommand(_ => ClearAll(), _ => !IsBusy && _iconOptions.Count > 0);
            RefreshCommand = new AsyncRelayCommand(ReloadAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(BinaryPath));
        }

        public event Action? RequestClose;

        public ReadOnlyObservableCollection<ExecutableIconOption> IconOptions => _readOnlyOptions;

        public string BinaryPath
        {
            get => _binaryPath;
            private set
            {
                if (SetField(ref _binaryPath, value))
                {
                    OnPropertyChanged(nameof(BinaryFileName));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string BinaryFileName => string.IsNullOrWhiteSpace(BinaryPath)
            ? string.Empty
            : Path.GetFileName(BinaryPath);

        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                if (SetField(ref _outputDirectory, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetField(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(IsEmptyStateVisible));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsExporting
        {
            get => _isExporting;
            private set
            {
                if (SetField(ref _isExporting, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsBusy => IsLoading || IsExporting;

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        public bool HasIcons => _iconOptions.Count > 0;

        public bool IsEmptyStateVisible => !IsLoading && _iconOptions.Count == 0;

        public RelayCommand BrowseOutputCommand { get; }
        public AsyncRelayCommand ExportCommand { get; }
        public RelayCommand CloseCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand ClearAllCommand { get; }
        public AsyncRelayCommand RefreshCommand { get; }

        /// <summary>
        /// 设置要扫描的可执行文件路径并启动扫描。
        /// </summary>
        public void Initialize(string binaryPath)
        {
            BinaryPath = binaryPath;
            OutputDirectory = BuildDefaultOutputDirectory(binaryPath);
            _ = ReloadAsync();
        }

        private string BuildDefaultOutputDirectory(string binaryPath)
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var baseName = Path.GetFileNameWithoutExtension(binaryPath);
            var folderName = string.IsNullOrWhiteSpace(baseName) ? "icon-output" : $"{baseName}-output";
            return Path.Combine(desktop, folderName);
        }

        private async Task ReloadAsync()
        {
            if (string.IsNullOrWhiteSpace(BinaryPath))
            {
                return;
            }

            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                IsLoading = true;
                StatusMessage = "正在扫描图标资源...";
                _iconOptions.Clear();

                var icons = await _icoConverterService.ScanExecutableIconsAsync(BinaryPath, token);
                foreach (var icon in icons)
                {
                    var preview = CreatePreview(icon.IconBytes);
                    var option = new ExecutableIconOption(icon.DisplayName, icon.Frames, icon.IconBytes, preview);
                    option.PropertyChanged += (_, __) => CommandManager.InvalidateRequerySuggested();
                    _iconOptions.Add(option);
                }

                StatusMessage = _iconOptions.Count == 0
                    ? "未找到任何图标资源"
                    : $"已发现 {_iconOptions.Count} 个图标组合";
            }
            catch (OperationCanceledException)
            {
                // 忽略取消
            }
            catch (Exception ex)
            {
                StatusMessage = "扫描失败";
                CustomMessageBox.Show($"扫描图标失败: {ex.Message}", "图标提取器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(HasIcons));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
            }
        }

        private static BitmapSource CreatePreview(byte[] iconBytes)
        {
            try
            {
                using var ms = new MemoryStream(iconBytes, writable: false);
                var decoder = new IconBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault();
                if (frame != null && frame.CanFreeze)
                {
                    frame.Freeze();
                    return frame;
                }
            }
            catch
            {
                // 忽略并返回默认占位图
            }

            var placeholder = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0 }, 4);
            if (placeholder.CanFreeze)
            {
                placeholder.Freeze();
            }

            return placeholder;
        }

        private bool CanExport()
        {
            return !IsBusy
                   && _iconOptions.Any(option => option.IsSelected)
                   && !string.IsNullOrWhiteSpace(OutputDirectory);
        }

        private async Task ExportAsync()
        {
            if (!CanExport())
            {
                return;
            }

            var selected = _iconOptions.Where(option => option.IsSelected).ToList();
            if (selected.Count == 0)
            {
                CustomMessageBox.Show("请至少选择一个图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                IsExporting = true;
                StatusMessage = "正在导出选定的图标...";
                Directory.CreateDirectory(OutputDirectory);
                var baseName = Path.GetFileNameWithoutExtension(BinaryPath);

                await Task.Run(() =>
                {
                    for (int i = 0; i < selected.Count; i++)
                    {
                        var option = selected[i];
                        var fileName = BuildIconFileName(baseName, option, i + 1);
                        var targetPath = Path.Combine(OutputDirectory, fileName);
                        File.WriteAllBytes(targetPath, option.IconBytes);
                    }
                });

                StatusMessage = $"导出完成，共 {selected.Count} 个图标。";
                CustomMessageBox.Show(StatusMessage, "图标提取器", MessageBoxButton.OK, MessageBoxImage.Information);
                //RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = "导出失败";
                CustomMessageBox.Show($"导出失败: {ex.Message}", "图标提取器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsExporting = false;
            }
        }

        private static string BuildIconFileName(string? baseName, ExecutableIconOption option, int index)
        {
            var safeBase = string.IsNullOrWhiteSpace(baseName) ? "icon" : SanitizeFileName(baseName);
            var sizeLabel = string.IsNullOrWhiteSpace(option.PrimarySizeLabel)
                ? "var"
                : option.PrimarySizeLabel.Replace('×', 'x');
            return $"{safeBase}_{index:D2}_{sizeLabel}.ico";
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "icon" : sanitized;
        }

        private void BrowseOutput()
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "选择导出目录",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(OutputDirectory) ? OutputDirectory : Path.GetDirectoryName(OutputDirectory) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                OutputDirectory = dialog.SelectedPath;
            }
        }

        private void SelectAll()
        {
            foreach (var option in _iconOptions)
            {
                option.IsSelected = true;
            }
        }

        private void ClearAll()
        {
            foreach (var option in _iconOptions)
            {
                option.IsSelected = false;
            }
        }
    }
}
