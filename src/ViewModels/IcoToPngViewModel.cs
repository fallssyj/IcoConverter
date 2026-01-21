using IcoConverter.Models;
using IcoConverter.Services;
using IcoConverter.Utils;
using IcoConverter;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace IcoConverter.ViewModels
{
    /// <summary>
    /// ICO 转 PNG 弹窗的视图模型。
    /// </summary>
    public class IcoToPngViewModel : ViewModelBase
    {
        private readonly IIcoPngConversionService _icoPngConversionService;
        private string _icoPath = string.Empty;
        private string _outputPath = string.Empty;
        private bool _isProcessing;
        private string _statusMessage = "请选择分辨率并导出。";

        /// <summary>
        /// 请求关闭窗口事件。
        /// </summary>
        public event Action? RequestClose;

        public IcoToPngViewModel(IIcoPngConversionService icoPngConversionService)
        {
            _icoPngConversionService = icoPngConversionService;

            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            ConvertCommand = new AsyncRelayCommand(ConvertAsync, CanConvert);
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            ClearAllCommand = new RelayCommand(_ => ClearAll());

            Resolutions.CollectionChanged += (_, __) => CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// ICO 文件路径。
        /// </summary>
        public string IcoPath
        {
            get => _icoPath;
            private set => SetField(ref _icoPath, value);
        }

        /// <summary>
        /// 输出目录。
        /// </summary>
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (SetField(ref _outputPath, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 是否正在处理导出。
        /// </summary>
        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (SetField(ref _isProcessing, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 状态提示文本。
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        /// <summary>
        /// ICO 包含的分辨率列表。
        /// </summary>
        public ObservableCollection<IcoResolution> Resolutions { get; } = new();

        public RelayCommand BrowseOutputCommand { get; }
        public AsyncRelayCommand ConvertCommand { get; }
        public RelayCommand CloseCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand ClearAllCommand { get; }

        /// <summary>
        /// 初始化 ICO 信息并加载分辨率。
        /// </summary>
        public void Initialize(string icoPath)
        {
            if (string.IsNullOrWhiteSpace(icoPath))
            {
                return;
            }

            try
            {
                IcoPath = icoPath;

                // 默认输出目录为 ICO 同级目录下的 output
                var icoDirectory = Path.GetDirectoryName(icoPath) ?? string.Empty;
                OutputPath = Path.Combine(icoDirectory, "output");

                Resolutions.Clear();
                foreach (var resolution in _icoPngConversionService.GetResolutions(icoPath))
                {
                    resolution.PropertyChanged += (_, __) => CommandManager.InvalidateRequerySuggested();
                    Resolutions.Add(resolution);
                }

                StatusMessage = $"已检测到 {Resolutions.Count} 个分辨率。";
            }
            catch (Exception ex)
            {
                StatusMessage = "读取 ICO 失败。";
                CustomMessageBox.Show($"读取 ICO 失败: {ex.Message}", "ICO 转 PNG", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 选择输出目录。
        /// </summary>
        private void BrowseOutput()
        {
            using var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "选择输出目录",
                UseDescriptionForTitle = true,
                SelectedPath = OutputPath
            };

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                OutputPath = dialog.SelectedPath;
            }
        }

        /// <summary>
        /// 执行转换。
        /// </summary>
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

                await _icoPngConversionService.ExportToPngAsync(IcoPath, OutputPath, Resolutions);

                StatusMessage = "导出完成。";
                CustomMessageBox.Show("导出完成。", "ICO 转 PNG", MessageBoxButton.OK, MessageBoxImage.Information);
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = "导出失败。";
                CustomMessageBox.Show($"导出失败: {ex.Message}", "ICO 转 PNG", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// 判断是否允许执行导出。
        /// </summary>
        private bool CanConvert()
        {
            return !IsProcessing
                   && !string.IsNullOrWhiteSpace(IcoPath)
                   && !string.IsNullOrWhiteSpace(OutputPath)
                   && Resolutions.Any(r => r.IsSelected);
        }

        /// <summary>
        /// 全选分辨率。
        /// </summary>
        private void SelectAll()
        {
            foreach (var resolution in Resolutions)
            {
                resolution.IsSelected = true;
            }
        }

        /// <summary>
        /// 清空分辨率选择。
        /// </summary>
        private void ClearAll()
        {
            foreach (var resolution in Resolutions)
            {
                resolution.IsSelected = false;
            }
        }
    }
}
