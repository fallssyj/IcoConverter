namespace IcoConverter.Services
{
    /// <summary>
    /// 应用设置模型。
    /// </summary>
    public class AppSettings
    {
        public bool IsDarkTheme { get; set; }

        public double TargetDpi { get; set; } = 96d;
    }
}