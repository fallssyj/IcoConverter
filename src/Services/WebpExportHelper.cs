using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IcoConverter.Models;
using SkiaSharp;

namespace IcoConverter.Services
{
    // WPF 内置编码器不支持 WebP，交由 SkiaSharp 处理
    internal static class WebpExportHelper
    {
        internal static void Save(BitmapSource source, Stream destination, WebpExportOptions options)
        {
            using var bitmap = CreateSkBitmap(source);
            using var pixmap = bitmap.PeekPixels()
                ?? throw new InvalidOperationException("WebP 编码失败：像素不可读。");

            // 有损模式使用所选质量。无损模式以最大压缩强度编码。
            var quality = options.IsLossless
                ? WebpExportOptions.MaxQuality
                : Math.Clamp(options.Quality, WebpExportOptions.MinQuality, WebpExportOptions.MaxQuality);

            var encoderOptions = new SKWebpEncoderOptions(
                options.IsLossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy,
                quality);

            if (!pixmap.Encode(destination, encoderOptions))
            {
                throw new InvalidOperationException("WebP 编码失败。");
            }
        }

        private static SKBitmap CreateSkBitmap(BitmapSource source)
        {
            if (source.Format != PixelFormats.Pbgra32)
            {
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = source;
                converted.DestinationFormat = PixelFormats.Pbgra32;
                converted.EndInit();
                if (converted.CanFreeze)
                {
                    converted.Freeze();
                }

                source = converted;
            }

            // Skia 位图采用相同的预乘 BGRA 布局，可直接复制像素。
            var info = new SKImageInfo(
                source.PixelWidth,
                source.PixelHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);

            var bitmap = new SKBitmap(info);

            try
            {
                source.CopyPixels(
                    new System.Windows.Int32Rect(0, 0, info.Width, info.Height),
                    bitmap.GetPixels(),
                    info.BytesSize,
                    info.RowBytes);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }

            return bitmap;
        }
    }
}
