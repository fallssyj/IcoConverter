using IcoConverter.Models;
using System.Text.Json.Serialization;

namespace IcoConverter.Services
{
    // 记录界面、预览和导出偏好。当前图片的蒙版、画布及变换参数保留在会话内。
    public class AppSettings
    {
        public bool IsDarkTheme { get; set; }

        // 以字符串存储（"Mica" / "Acrylic" / "Solid"），便于直接查看 settings.json
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WindowMaterial Material { get; set; } = WindowMaterial.Mica;

        public double TargetDpi { get; set; } = 96d;

        public bool IsRealTimePreviewEnabled { get; set; } = true;

        public bool IsMaskSettingsExpanded { get; set; } = true;

        public bool IsIcoResolutionExpanded { get; set; } = true;

        public bool IsLogExpanded { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PreviewBackgroundMode PreviewBackground { get; set; } = PreviewBackgroundMode.Checker;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public IcoPngFrameMode IcoPngFrameMode { get; set; } = IcoPngFrameMode.Auto;

        public int TrimPaddingPercent { get; set; } = 8;
    }
}
