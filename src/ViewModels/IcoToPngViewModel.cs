using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;
using IcoConverter.Services;
using IcoConverter;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace IcoConverter.ViewModels
{
    /// <summary>
    /// ICO / ICNS 拆帧导出 PNG。两种容器的读取和写出都由 <see cref="IIconPngConversionService"/> 分派。
    /// </summary>
    public class IcoToPngViewModel : ObservableObject
    {
        private readonly IIconPngConversionService _iconPngConversionService;
        private string _iconPath = string.Empty;
        private string _formatName = "ICO";
        private string _outputPath = string.Empty;
        private bool _isProcessing;
        private string _statusMessage = "请选择尺寸并导出。";

        public event Action? RequestClose;

        public IcoToPngViewModel(IIconPngConversionService iconPngConversionService)
        {
            _iconPngConversionService = iconPngConversionService;

            BrowseOutputCommand = new RelayCommand(BrowseOutput);
            ConvertCommand = new AsyncRelayCommand(ConvertAsync, CanConvert);
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            SelectAllCommand = new RelayCommand(SelectAll);
            ClearAllCommand = new RelayCommand(ClearAll);

            Resolutions.CollectionChanged += (_, __) => NotifyCommandStateChanges();
        }

        public string IconPath
        {
            get => _iconPath;
            private set => SetProperty(ref _iconPath, value);
        }

        public string FormatName
        {
            get => _formatName;
            private set
            {
                if (SetProperty(ref _formatName, value))
                {
                    OnPropertyChanged(nameof(HeaderText));
                    OnPropertyChanged(nameof(PathLabel));
                    OnPropertyChanged(nameof(ResolutionsLabel));
                }
            }
        }

        public string HeaderText => $"{FormatName} 转 PNG";

        public string PathLabel => $"{FormatName} 路径:";

        public string ResolutionsLabel => $"{FormatName} 包含的尺寸";

        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetProperty(ref _outputPath, value))
                {
                    NotifyCommandStateChanges();
                }
            }
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    NotifyCommandStateChanges();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<IcoResolution> Resolutions { get; } = new();

        private IcoResolution? _selectedResolution;

        /// <summary>右侧放大的当前帧，随列表选中项变化。</summary>
        public IcoResolution? SelectedResolution
        {
            get => _selectedResolution;
            set
            {
                if (SetProperty(ref _selectedResolution, value))
                {
                    OnPropertyChanged(nameof(SelectedPreviewImage));
                    OnPropertyChanged(nameof(SelectedPreviewLabel));
                }
            }
        }

        public System.Windows.Media.Imaging.BitmapSource? SelectedPreviewImage => _selectedResolution?.Preview;

        public string SelectedPreviewLabel => _selectedResolution == null
            ? "在左边选一档看效果"
            : $"{_selectedResolution.Width} × {_selectedResolution.Height} px";

        public IRelayCommand BrowseOutputCommand { get; }
        public IAsyncRelayCommand ConvertCommand { get; }
        public IRelayCommand CloseCommand { get; }
        public IRelayCommand SelectAllCommand { get; }
        public IRelayCommand ClearAllCommand { get; }

        public void Initialize(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return;
            }

            try
            {
                FormatName = Path.GetExtension(iconPath).TrimStart('.').ToUpperInvariant();

                IconPath = iconPath;

                // 初始化时将默认输出目录设为源文件同级的 output。
                var iconDirectory = Path.GetDirectoryName(iconPath) ?? string.Empty;
                OutputPath = Path.Combine(iconDirectory, "output");

                Resolutions.Clear();
                foreach (var resolution in _iconPngConversionService.GetResolutions(iconPath))
                {
                    resolution.PropertyChanged += (_, __) => NotifyCommandStateChanges();
                    Resolutions.Add(resolution);
                }

                StatusMessage = $"已检测到 {Resolutions.Count} 个尺寸。";

                // 初始预览列表中的第一个尺寸，保留各项的导出勾选状态。
                SelectedResolution = Resolutions.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusMessage = $"读取 {FormatName} 失败。";
                AppMessageBox.Show($"读取 {FormatName} 失败: {ex.Message}", HeaderText, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BrowseOutput()
        {
            var initialDirectory = Directory.Exists(OutputPath)
                ? OutputPath
                : Path.GetDirectoryName(OutputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择输出目录",
                InitialDirectory = initialDirectory
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                OutputPath = dialog.FolderName;
            }
        }

        private async Task ConvertAsync()
        {
            if (IsProcessing)
            {
                return;
            }

            try
            {
                IsProcessing = true;
                StatusMessage = "正在导出 PNG...";

                await _iconPngConversionService.ExportToPngAsync(IconPath, OutputPath, Resolutions);

                StatusMessage = "导出完成。";
                AppMessageBox.Show("导出完成。", HeaderText, MessageBoxButton.OK, MessageBoxImage.Information);
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = "导出失败。";
                AppMessageBox.Show($"导出失败: {ex.Message}", HeaderText, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private bool CanConvert()
        {
            return !IsProcessing
                   && !string.IsNullOrWhiteSpace(IconPath)
                   && !string.IsNullOrWhiteSpace(OutputPath)
                   && Resolutions.Any(r => r.IsSelected);
        }

        private void NotifyCommandStateChanges()
        {
            ConvertCommand.NotifyCanExecuteChanged();
        }

        private void SelectAll()
        {
            foreach (var resolution in Resolutions)
            {
                resolution.IsSelected = true;
            }
        }

        private void ClearAll()
        {
            foreach (var resolution in Resolutions)
            {
                resolution.IsSelected = false;
            }
        }
    }
}
