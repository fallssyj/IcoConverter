using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    // OriginalDpiX 和 OriginalDpiY 记录解码后、归一化前的位图 DPI。
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