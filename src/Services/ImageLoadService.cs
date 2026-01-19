using SkiaSharp;
using Svg.Skia;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    /// <summary>
    /// 图片加载服务实现：支持 SVG 与常规位图，统一 DPI。
    /// </summary>
    public class ImageLoadService : IImageLoadService
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".svg"
        };

        public bool IsSupportedImageFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            return SupportedExtensions.Contains(extension) || IsSvgFile(filePath);
        }

        public ImageLoadResult LoadImage(string filePath, double targetDpi, int svgRenderMaxSize)
        {
            var loaded = IsSvgFile(filePath)
                ? LoadSvgBitmap(filePath, targetDpi, svgRenderMaxSize)
                : LoadRasterBitmap(filePath);

            var normalized = NormalizeDpi(loaded, targetDpi);
            return new ImageLoadResult(normalized, loaded.DpiX, loaded.DpiY);
        }

        /// <summary>
        /// 判断文件是否为 SVG（扩展名或文件头）。
        /// </summary>
        private static bool IsSvgFile(string filePath)
        {
            if (string.Equals(Path.GetExtension(filePath), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                using var reader = new StreamReader(filePath);
                var buffer = new char[512];
                var read = reader.Read(buffer, 0, buffer.Length);
                var header = new string(buffer, 0, read);
                return header.Contains("<svg", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 加载常规位图文件。
        /// </summary>
        private static BitmapSource LoadRasterBitmap(string filePath)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }

        /// <summary>
        /// 通过 SVG 渲染生成位图。
        /// </summary>
        private static BitmapSource LoadSvgBitmap(string filePath, double targetDpi, int svgRenderMaxSize)
        {
            var svg = new SKSvg();
            using var stream = File.OpenRead(filePath);
            var picture = svg.Load(stream);
            if (picture == null)
            {
                throw new InvalidOperationException("SVG 解析失败。");
            }

            var bounds = picture.CullRect;
            var width = bounds.Width;
            var height = bounds.Height;
            if (width <= 0 || height <= 0)
            {
                width = svgRenderMaxSize;
                height = svgRenderMaxSize;
            }

            var scale = Math.Min(svgRenderMaxSize / width, svgRenderMaxSize / height);
            var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var skBitmap = new SKBitmap(info);
            using (var canvas = new SKCanvas(skBitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(scale);
                canvas.Translate(-bounds.Left, -bounds.Top);
                canvas.DrawPicture(picture);
                canvas.Flush();
            }

            var bitmapSource = BitmapSource.Create(
                skBitmap.Width,
                skBitmap.Height,
                targetDpi,
                targetDpi,
                PixelFormats.Bgra32,
                null,
                skBitmap.Bytes,
                skBitmap.RowBytes);

            if (bitmapSource.CanFreeze)
            {
                bitmapSource.Freeze();
            }

            return bitmapSource;
        }

        /// <summary>
        /// 将图片 DPI 归一化为指定值。
        /// </summary>
        private static BitmapSource NormalizeDpi(BitmapSource source, double targetDpi)
        {
            if (Math.Abs(source.DpiX - targetDpi) < 0.01 && Math.Abs(source.DpiY - targetDpi) < 0.01)
            {
                return source;
            }

            var stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            var normalized = BitmapSource.Create(
                source.PixelWidth,
                source.PixelHeight,
                targetDpi,
                targetDpi,
                source.Format,
                source.Palette,
                pixels,
                stride);

            if (normalized.CanFreeze)
            {
                normalized.Freeze();
            }

            return normalized;
        }
    }
}