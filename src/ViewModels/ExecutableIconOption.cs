using System;
using IcoConverter.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace IcoConverter.ViewModels
{
    /// <summary>
    /// 可执行文件图标候选项，支持勾选导出。
    /// </summary>
    public class ExecutableIconOption : ViewModelBase
    {
        private bool _isSelected = true;

        public ExecutableIconOption(string displayName, IReadOnlyList<IconFrameInfo> frames, byte[] iconBytes, BitmapSource preview)
        {
            DisplayName = displayName;
            Frames = frames ?? Array.Empty<IconFrameInfo>();
            IconBytes = iconBytes;
            Preview = preview;

            if (Frames.Count > 0)
            {
                var primary = Frames.OrderByDescending(f => f.Width * f.Height).First();
                PrimarySizeLabel = $"{primary.Width}×{primary.Height}";
                Details = string.Join(", ", Frames.Select(f => f.DisplayText));
            }
            else
            {
                PrimarySizeLabel = "未知";
                Details = string.Empty;
            }
        }

        public string DisplayName { get; }

        public IReadOnlyList<IconFrameInfo> Frames { get; }

        public byte[] IconBytes { get; }

        public BitmapSource Preview { get; }

        public string PrimarySizeLabel { get; } = string.Empty;

        public string Details { get; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }
    }
}
