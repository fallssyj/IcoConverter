using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public interface IImageLoadService
    {
        bool IsSupportedImageFile(string filePath);

        ImageLoadResult LoadImage(string filePath, double targetDpi, int svgRenderMaxSize);
    }
}