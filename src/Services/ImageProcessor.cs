using IcoConverter.ViewModels;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public class ImageProcessor : IImageProcessor
    {
        /// <summary>
        /// 对提供的 <see cref="BitmapSource"/> 应用圆角遮罩并返回一个新的 <see cref="BitmapSource"/>。
        /// 返回的图像与原图保持相同的尺寸与 DPI，使用 <see cref="RenderTargetBitmap"/> 渲染并冻结结果，
        /// 以确保可安全在多个线程间使用。
        /// </summary>
        public BitmapSource ApplyRoundedCorners(BitmapSource source, int cornerRadius, CornerQuality quality)
        {
            if (cornerRadius <= 0)
                return source;

            try
            {
                // 创建DrawingVisual来绘制带圆角的图像
                var drawingVisual = new DrawingVisual();
                ApplyQualitySettings(drawingVisual, quality);
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    // 创建圆角矩形几何图形
                    var rect = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
                    var geometry = CreateRoundedRectangleGeometry(rect, cornerRadius);

                    // 使用几何图形作为剪辑区域绘制图像
                    drawingContext.PushClip(geometry);
                    drawingContext.DrawImage(source, rect);
                }

                // 渲染到RenderTargetBitmap并冻结以便跨线程使用
                var renderTarget = new RenderTargetBitmap(
                    source.PixelWidth,
                    source.PixelHeight,
                    source.DpiX,
                    source.DpiY,
                    PixelFormats.Pbgra32);

                renderTarget.Render(drawingVisual);
                if (renderTarget.CanFreeze)
                    renderTarget.Freeze();

                return renderTarget;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"应用圆角时出错: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 创建表示带圆角矩形的几何图形。
        /// 使用WPF内置的RectangleGeometry，它提供了高质量的圆角矩形实现。
        /// </summary>
        private static RectangleGeometry CreateRoundedRectangleGeometry(Rect rect, int cornerRadius)
        {
            return new RectangleGeometry(rect, cornerRadius, cornerRadius);
        }

        /// <summary>
        /// 根据质量选项设置渲染提示，以平衡性能和边缘平滑度。
        /// </summary>
        private static void ApplyQualitySettings(DrawingVisual visual, CornerQuality quality)
        {
            switch (quality)
            {
                case CornerQuality.Low:
                    RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.LowQuality);
                    break;
                case CornerQuality.Medium:
                    RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.Linear);
                    break;
                default:
                    RenderOptions.SetEdgeMode(visual, EdgeMode.Unspecified);
                    RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                    break;
            }
        }
    }
}
