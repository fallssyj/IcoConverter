using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace IcoConverter.Services
{
    // ICO、ICNS 和 PNG 图标集共用的位图转换和重采样。
    internal static class BitmapExportHelper
    {
        internal static Bitmap ToBitmap(BitmapSource source)
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

            // 两种格式均使用预乘 BGRA，可按目标位图的步幅直接复制像素。
            var bitmap = new Bitmap(source.PixelWidth, source.PixelHeight, DrawingPixelFormat.Format32bppPArgb);

            var data = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly,
                bitmap.PixelFormat);

            try
            {
                source.CopyPixels(
                    System.Windows.Int32Rect.Empty,
                    data.Scan0,
                    data.Height * data.Stride,
                    data.Stride);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        // 以源图长边为边长建立正方形画布，将源图居中并补透明边。
        internal static Bitmap FitToSquare(Bitmap source)
        {
            var size = Math.Max(source.Width, source.Height);
            using var square = new Bitmap(size, size, DrawingPixelFormat.Format32bppPArgb);

            using (var graphics = Graphics.FromImage(square))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                var scale = (float)size / Math.Max(source.Width, source.Height);
                var scaledWidth = source.Width * scale;
                var scaledHeight = source.Height * scale;
                var offsetX = (size - scaledWidth) / 2f;
                var offsetY = (size - scaledHeight) / 2f;
                graphics.DrawImage(source, offsetX, offsetY, scaledWidth, scaledHeight);
            }

            return Ensure32bppArgb(square);
        }

        internal static Bitmap Resize(Bitmap original, int width, int height)
        {
            // 使用预乘 alpha 重采样，减少透明边缘插值产生的暗边。
            using var resized = new Bitmap(width, height, DrawingPixelFormat.Format32bppPArgb);

            using (var graphics = Graphics.FromImage(resized))
            {
                // 缩小时使用双线性以减少硬边振铃。其余情况使用双三次。
                var shrinking = width < original.Width || height < original.Height;
                graphics.InterpolationMode = shrinking
                    ? InterpolationMode.HighQualityBilinear
                    : InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.CompositingMode = CompositingMode.SourceCopy;

                graphics.DrawImage(original, 0, 0, width, height);
            }

            return Ensure32bppArgb(resized);
        }

        internal static Bitmap Ensure32bppArgb(Bitmap source)
        {
            if (source.PixelFormat == DrawingPixelFormat.Format32bppArgb)
            {
                return (Bitmap)source.Clone();
            }

            var converted = new Bitmap(source.Width, source.Height, DrawingPixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(converted);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            return converted;
        }

        internal static byte[] EncodePng(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }
    }
}
