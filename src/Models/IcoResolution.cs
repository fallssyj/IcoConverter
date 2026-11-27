using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media.Imaging;

namespace IcoConverter.Models
{
    public class IcoResolution(int width, int height, bool isSelected = true) : ObservableObject
    {
        private int _width = width;
        private int _height = height;
        private bool _isSelected = isSelected;
        private string _hint = string.Empty;

        /// <summary>该尺寸的缩略图。尚未生成或无法生成时为 null。</summary>
        public BitmapSource? Preview { get; set; }

        public int Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public int Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string DisplayText => $"{Width}×{Height}";

        // ICO 目录项宽高各占 1 字节，256 以 0 编码，超出后无法表示
        public const int IcoMaxSize = 256;

        public bool IsBeyondIcoLimit => Width > IcoMaxSize || Height > IcoMaxSize;

        // ICNS 支持的方形像素尺寸。多个类型标识可以对应同一尺寸。
        public static readonly int[] IcnsSizes = [16, 32, 64, 128, 256, 512, 1024];

        public bool IsSupportedByIcns => Width == Height && IcnsSizes.Contains(Width);

        /// <summary>导出提示文字，由选择窗口按目标格式填充。空串表示无提示。</summary>
        public string Hint
        {
            get => _hint;
            set
            {
                if (SetProperty(ref _hint, value))
                {
                    OnPropertyChanged(nameof(HasHint));
                }
            }
        }

        public bool HasHint => !string.IsNullOrEmpty(_hint);

        public void ApplyFormatHint(IconExportFormat format)
        {
            // ICNS 不支持的档位已在窗口中过滤，PNG 集无容器限制
            Hint = format == IconExportFormat.Ico && IsBeyondIcoLimit
                ? "扩展尺寸 · 兼容性有限"
                : string.Empty;
        }
    }
}
