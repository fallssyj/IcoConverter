using System.Windows.Media.Imaging;
using IcoConverter.ViewModels;

namespace IcoConverter.Services
{
    /// <summary>
    /// 供视图模型使用的图像处理操作接口。
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// 对给定的 <see cref="BitmapSource"/> 应用圆角处理。
        /// </summary>
        BitmapSource ApplyRoundedCorners(BitmapSource source, int cornerRadius, CornerQuality quality);
    }
}
