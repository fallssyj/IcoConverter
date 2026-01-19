using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    /// <summary>
    /// 图片加载服务接口（支持 SVG 与位图）。
    /// </summary>
    public interface IImageLoadService
    {
        /// <summary>
        /// 判断是否为支持的图片文件。
        /// </summary>
        bool IsSupportedImageFile(string filePath);

        /// <summary>
        /// 加载并按目标 DPI 归一化图片。
        /// </summary>
        ImageLoadResult LoadImage(string filePath, double targetDpi, int svgRenderMaxSize);
    }
}