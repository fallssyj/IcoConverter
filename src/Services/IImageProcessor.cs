using System.Windows.Media.Imaging;
using IcoConverter.Models;

namespace IcoConverter.Services
{
    /// <summary>
    /// 供视图模型使用的图像处理操作接口。
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// 对给定的 <see cref="BitmapSource"/> 按指定蒙版形状进行裁剪，并根据图像变换参数缩放/平移源图像。
        /// </summary>
        BitmapSource ApplyMask(
            BitmapSource source,
            MaskShape shape,
            int cornerRadius,
            int polygonSides,
            double polygonRotationDegrees,
            ImageTransformOptions transformOptions);
    }
}
