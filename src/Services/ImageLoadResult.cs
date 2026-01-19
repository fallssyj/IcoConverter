using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    /// <summary>
    /// 图片加载结果，包含归一化后的图像以及原始 DPI 信息。
    /// </summary>
    public sealed class ImageLoadResult
    {
        public ImageLoadResult(BitmapSource image, double originalDpiX, double originalDpiY)
        {
            Image = image;
            OriginalDpiX = originalDpiX;
            OriginalDpiY = originalDpiY;
        }

        public BitmapSource Image { get; }

        public double OriginalDpiX { get; }

        public double OriginalDpiY { get; }
    }
}