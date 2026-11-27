using CommunityToolkit.Mvvm.ComponentModel;
using System;
using IcoConverter.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;

namespace IcoConverter.ViewModels
{
    public class ExecutableIconOption : ObservableObject
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
                FrameSummary = $"{Frames.Count} 帧 · 最大 {PrimarySizeLabel}";
                FrameLines = Frames.Select(f => f.DisplayText).ToArray();
            }
            else
            {
                PrimarySizeLabel = "未知";
                Details = string.Empty;
                FrameSummary = "帧信息读取失败";
                FrameLines = Array.Empty<string>();
            }
        }

        public string DisplayName { get; }

        // 卡片显示内容即为资源 ID，右侧标题需注明其为资源名
        public string Title => $"资源 {DisplayName}";

        public string FrameSummary { get; }

        public IReadOnlyList<string> FrameLines { get; }

        public IReadOnlyList<IconFrameInfo> Frames { get; }

        public byte[] IconBytes { get; }

        public BitmapSource Preview { get; }

        public string PrimarySizeLabel { get; } = string.Empty;

        public string Details { get; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
