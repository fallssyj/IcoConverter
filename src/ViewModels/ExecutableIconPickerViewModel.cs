using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IcoConverter.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IcoConverter.ViewModels
{
    public class ExecutableIconPickerViewModel : ObservableObject
    {
        private readonly ExecutableIconReader _iconReader;
        private readonly ObservableCollection<ExecutableIconOption> _iconOptions = new();
        private readonly ReadOnlyObservableCollection<ExecutableIconOption> _readOnlyOptions;
        private string _binaryPath = string.Empty;
        private string _outputDirectory = string.Empty;
        private string _statusMessage = "等待扫描可执行文件";
        private bool _isLoading;
        private bool _isExporting;
        private ExecutableIconOption? _previewOption;
        private ExecutableIconOption? _anchorOption;
        private CancellationTokenSource? _loadCts;

        public ExecutableIconPickerViewModel(ExecutableIconReader iconReader)
        {
            _iconReader = iconReader;

            _readOnlyOptions = new ReadOnlyObservableCollection<ExecutableIconOption>(_iconOptions);

            _iconOptions.CollectionChanged += (_, __) =>
            {
                OnPropertyChanged(nameof(IconOptions));
                OnPropertyChanged(nameof(HasIcons));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                NotifyCommandStateChanges();
            };

            BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
            ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            SelectAllCommand = new RelayCommand(SelectAll, () => !IsBusy && _iconOptions.Count > 0);
            ClearAllCommand = new RelayCommand(ClearAll, () => !IsBusy && _iconOptions.Count > 0);
            RefreshCommand = new AsyncRelayCommand(ReloadAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(BinaryPath));
        }

        public event Action? RequestClose;

        public ReadOnlyObservableCollection<ExecutableIconOption> IconOptions => _readOnlyOptions;

        public string BinaryPath
        {
            get => _binaryPath;
            private set
            {
                if (SetProperty(ref _binaryPath, value))
                {
                    OnPropertyChanged(nameof(BinaryFileName));
                    NotifyCommandStateChanges();
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
                if (SetProperty(ref _outputDirectory, value))
                {
                    NotifyCommandStateChanges();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(IsEmptyStateVisible));
                    NotifyCommandStateChanges();
                }
            }
        }

        public bool IsExporting
        {
            get => _isExporting;
            private set
            {
                if (SetProperty(ref _isExporting, value))
                {
                    OnPropertyChanged(nameof(IsBusy));
                    NotifyCommandStateChanges();
                }
            }
        }

        public bool IsBusy => IsLoading || IsExporting;

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public bool HasIcons => _iconOptions.Count > 0;

        public bool IsEmptyStateVisible => !IsLoading && _iconOptions.Count == 0;

        public ExecutableIconOption? PreviewOption
        {
            get => _previewOption;
            private set
            {
                if (SetProperty(ref _previewOption, value))
                {
                    OnPropertyChanged(nameof(HasPreviewOption));
                }
            }
        }

        public bool HasPreviewOption => _previewOption != null;

        public bool IsDarkTheme => AppThemeService.IsDark;

        public void RefreshTheme() => OnPropertyChanged(nameof(IsDarkTheme));

        public string SelectionSummary => $"已选 {_iconOptions.Count(o => o.IsSelected)} / {_iconOptions.Count}";

        public IRelayCommand BrowseOutputCommand { get; }
        public IAsyncRelayCommand ExportCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public IRelayCommand SelectAllCommand { get; }
        public IRelayCommand ClearAllCommand { get; }
        public IAsyncRelayCommand RefreshCommand { get; }

        public void Initialize(string binaryPath)
        {
            BinaryPath = binaryPath;
            OutputDirectory = BuildDefaultOutputDirectory(binaryPath);
            _ = ReloadAsync();
        }

        // 悬停项优先作为预览目标，移出后回退到最后选中项
        public void HoverOption(ExecutableIconOption? option)
        {
            PreviewOption = option ?? _anchorOption ?? _iconOptions.FirstOrDefault();
        }

        public void AnchorOption(ExecutableIconOption option)
        {
            _anchorOption = option;
            PreviewOption = option;
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
                _anchorOption = null;
                PreviewOption = null;

                var icons = await _iconReader.ScanExecutableIconsAsync(BinaryPath, token);
                foreach (var icon in icons)
                {
                    var preview = CreatePreview(icon.IconBytes);
                    var option = new ExecutableIconOption(icon.DisplayName, icon.Frames, icon.IconBytes, preview);
                    option.PropertyChanged += (_, __) => NotifyCommandStateChanges();
                    _iconOptions.Add(option);
                }

                _anchorOption = _iconOptions.FirstOrDefault();
                PreviewOption = _anchorOption;
                OnPropertyChanged(nameof(SelectionSummary));

                StatusMessage = _iconOptions.Count == 0
                    ? "未找到任何图标资源"
                    : $"已发现 {_iconOptions.Count} 个图标组合";
            }
            catch (OperationCanceledException)
            {
                // 用户主动取消，不作为错误处理
            }
            catch (Exception ex)
            {
                StatusMessage = "扫描失败";
                AppMessageBox.Show($"扫描图标失败: {ex.Message}", "图标提取器", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // 解码失败时使用 1×1 透明图占位，避免列表加载中断
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

        private void NotifyCommandStateChanges()
        {
            BrowseOutputCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            SelectAllCommand.NotifyCanExecuteChanged();
            ClearAllCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SelectionSummary));
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
                AppMessageBox.Show("请至少选择一个图标。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
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
                AppMessageBox.Show(StatusMessage, "图标提取器", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = "导出失败";
                AppMessageBox.Show($"导出失败: {ex.Message}", "图标提取器", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var initialDirectory = Directory.Exists(OutputDirectory)
                ? OutputDirectory
                : Path.GetDirectoryName(OutputDirectory) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择导出目录",
                InitialDirectory = initialDirectory
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                OutputDirectory = dialog.FolderName;
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

