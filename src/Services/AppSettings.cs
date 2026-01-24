namespace IcoConverter.Services
{
    /// <summary>
    /// 应用设置模型。
    /// </summary>
    public class AppSettings
    {
        public bool IsDarkTheme { get; set; }

        public double TargetDpi { get; set; } = 96d;

        public bool IsRealTimePreviewEnabled { get; set; } = true;

        public bool IsMaskSettingsExpanded { get; set; } = true;

        public bool IsIcoResolutionExpanded { get; set; } = true;

        public bool IsLogExpanded { get; set; }

        public double LastPolygonRotation { get; set; }

        public int LastPolygonSides { get; set; } = 6;

        public int LastCornerRadius { get; set; } = 20;

        public double LastImageScale { get; set; } = 1d;

        public double LastImageOffsetX { get; set; }

        public double LastImageOffsetY { get; set; }
    }
}