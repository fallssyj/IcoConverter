using System.Windows.Media.Imaging;
using IcoConverter.Models;

namespace IcoConverter.Services
{
    public interface IImageProcessor
    {
        // 裁剪区域限制在图像边界内。有效选区覆盖整图时复用源位图。
        BitmapSource Crop(BitmapSource source, CropRegion region);

        // 找到可裁除的背景时，按主体外接矩形裁剪并按设定比例补透明边。
        BitmapSource TrimToContent(BitmapSource source, int paddingPercent);

        BitmapSource ApplyMask(BitmapSource source, ImageRenderOptions options);
    }
}
