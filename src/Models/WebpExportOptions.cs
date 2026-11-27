namespace IcoConverter.Models
{
    /// <summary>WebP 的编码参数：无损或按质量有损。</summary>
    public readonly record struct WebpExportOptions(bool IsLossless, int Quality)
    {
        public const int MinQuality = 0;

        public const int MaxQuality = 100;

        /// <summary>默认使用质量为 92 的有损编码。</summary>
        public static WebpExportOptions Default { get; } = new(false, 92);
    }
}
