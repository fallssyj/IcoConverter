using System;
using System.Runtime.InteropServices;
using IcoConverter.Models;
using SkiaSharp;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public class ImageProcessor : IImageProcessor
    {
        private const int SupersampleScale = 2;

        // 2 倍超采样使中间位图的像素数增至四倍。输出长边超过阈值时按单倍尺寸渲染。
        public const int SupersampleMaxSize = 1536;
        private static readonly SKSamplingOptions HighQualitySampling =
            new(SKFilterMode.Linear, SKMipmapMode.Linear);

        // 裁剪区域覆盖整个画布时直接复用原图，避免拷贝
        public BitmapSource Crop(BitmapSource source, CropRegion region)
        {
            ArgumentNullException.ThrowIfNull(source);

            var x = Math.Clamp(region.X, 0, Math.Max(0, source.PixelWidth - 1));
            var y = Math.Clamp(region.Y, 0, Math.Max(0, source.PixelHeight - 1));
            var width = Math.Clamp(region.Width, 1, source.PixelWidth - x);
            var height = Math.Clamp(region.Height, 1, source.PixelHeight - y);

            if (x == 0 && y == 0 && width == source.PixelWidth && height == source.PixelHeight)
            {
                return source;
            }

            var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
            if (cropped.CanFreeze)
            {
                cropped.Freeze();
            }

            return cropped;
        }

        public BitmapSource TrimToContent(BitmapSource source, int paddingPercent)
        {
            ArgumentNullException.ThrowIfNull(source);

            var normalized = EnsurePbgra32(source);
            var width = normalized.PixelWidth;
            var height = normalized.PixelHeight;
            if (width < 2 || height < 2)
            {
                return source;
            }

            var stride = width * 4;
            var buffer = new byte[stride * height];
            normalized.CopyPixels(buffer, stride, 0);

            // 存在 alpha 小于 250 的像素时按透明度判定背景，否则使用四角平均色。
            var hasTransparency = false;
            for (var offset = 3; offset < buffer.Length; offset += 4)
            {
                if (buffer[offset] < 250)
                {
                    hasTransparency = true;
                    break;
                }
            }

            int bgR = 0, bgG = 0, bgB = 0;
            if (!hasTransparency)
            {
                var corners = new[]
                {
                    0,
                    (width - 1) * 4,
                    (height - 1) * stride,
                    (height - 1) * stride + (width - 1) * 4
                };
                foreach (var index in corners)
                {
                    bgB += buffer[index];
                    bgG += buffer[index + 1];
                    bgR += buffer[index + 2];
                }

                bgR /= corners.Length;
                bgG /= corners.Length;
                bgB /= corners.Length;
            }

            const int colorTolerance = 30;
            var left = width;
            var top = height;
            var right = -1;
            var bottom = -1;

            for (var y = 0; y < height; y++)
            {
                var rowStart = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var index = rowStart + x * 4;
                    var isBackground = hasTransparency
                        ? buffer[index + 3] <= 8
                        : Math.Abs(buffer[index + 2] - bgR) + Math.Abs(buffer[index + 1] - bgG) + Math.Abs(buffer[index] - bgB) <= colorTolerance;

                    if (isBackground)
                    {
                        continue;
                    }

                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                }
            }

            // 整图均为背景，或主体已占满画布时无需裁剪
            if (right < left || bottom < top)
            {
                return source;
            }

            var contentWidth = right - left + 1;
            var contentHeight = bottom - top + 1;
            if (contentWidth == width && contentHeight == height)
            {
                return source;
            }

            // 留白比例以输出长边为基准。设为 10% 时，长边两端各留约 10%。
            var percent = Math.Clamp(paddingPercent, 0, 45);
            var longestSide = Math.Max(contentWidth, contentHeight);
            var pad = percent == 0
                ? 0
                : (int)Math.Round(longestSide * percent / (100d - percent * 2), MidpointRounding.AwayFromZero);
            var outputWidth = contentWidth + pad * 2;
            var outputHeight = contentHeight + pad * 2;

            using var sourceBitmap = CreateBitmap(normalized);
            using var output = new SKBitmap(new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(output))
            {
                canvas.Clear(SKColors.Transparent);
                using var image = SKImage.FromBitmap(sourceBitmap);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawImage(
                    image,
                    new SKRect(left, top, right + 1, bottom + 1),
                    new SKRect(pad, pad, pad + contentWidth, pad + contentHeight),
                    HighQualitySampling,
                    paint);
                canvas.Flush();
            }

            return CreateBitmapSource(output, source.DpiX, source.DpiY);
        }

        public BitmapSource ApplyMask(BitmapSource source, ImageRenderOptions options)
        {
            var normalizedSource = EnsurePbgra32(source);
            var sourceWidth = normalizedSource.PixelWidth;
            var sourceHeight = normalizedSource.PixelHeight;
            // 负值从两侧各裁去相同的像素数，短边至少保留 1 像素。
            var maxShrink = (Math.Min(sourceWidth, sourceHeight) - 1) / 2;
            var expand = Math.Clamp(options.CanvasExpandPixels, -maxShrink, 4096);
            var width = sourceWidth + expand * 2;
            var height = sourceHeight + expand * 2;
            using var maskPath = CreateMaskPath(
                options.Shape, width, height, options.Radius, options.CircleFitMode, options.CirclePaddingPercent);
            var hasMask = maskPath != null;
            var hasCanvasExpansion = expand != 0;
            var color = options.CanvasFillColor;
            var requiresProcessing = hasMask || !options.Transform.IsIdentity || hasCanvasExpansion || color.A > 0;
            if (!requiresProcessing)
            {
                return source;
            }

            var useSupersampling = options.EnableEdgeSupersampling && Math.Max(width, height) <= SupersampleMaxSize;
            var fillColor = new SKColor(color.R, color.G, color.B, color.A);

            using var sourceBitmap = CreateBitmap(normalizedSource);
            using var renderedBitmap = RenderMaskedBitmap(
                sourceBitmap,
                sourceWidth,
                sourceHeight,
                width,
                height,
                options.Shape,
                options.Radius,
                options.Transform,
                options.CircleFitMode,
                options.CirclePaddingPercent,
                expand,
                fillColor,
                options.FillCanvasOutsideMask,
                useSupersampling ? SupersampleScale : 1);

            if (useSupersampling)
            {
                using var downsampled = DownsampleBitmap(renderedBitmap, width, height);
                return CreateBitmapSource(downsampled, source.DpiX, source.DpiY);
            }

            return CreateBitmapSource(renderedBitmap, source.DpiX, source.DpiY);
        }

        private static SKBitmap CreateBitmap(BitmapSource source)
        {
            var info = new SKImageInfo(source.PixelWidth, source.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            var buffer = new byte[info.BytesSize];
            source.CopyPixels(new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight), buffer, info.RowBytes, 0);

            var bitmap = new SKBitmap(info);
            Marshal.Copy(buffer, 0, bitmap.GetPixels(), buffer.Length);
            return bitmap;
        }

        private static SKBitmap RenderMaskedBitmap(
            SKBitmap sourceBitmap,
            int sourceWidth,
            int sourceHeight,
            int width,
            int height,
            MaskShape shape,
            int cornerRadius,
            ImageTransformOptions transformOptions,
            CircleFitMode circleFitMode,
            int circlePaddingPercent,
            int canvasExpandPixels,
            SKColor canvasFillColor,
            bool fillCanvasOutsideMask,
            int renderScale)
        {
            var scaledWidth = Math.Max(1, width * renderScale);
            var scaledHeight = Math.Max(1, height * renderScale);
            var scaledCornerRadius = Math.Max(0, cornerRadius * renderScale);
            var scaledExpand = canvasExpandPixels * renderScale;
            var scaledSourceWidth = Math.Max(1, sourceWidth * renderScale);
            var scaledSourceHeight = Math.Max(1, sourceHeight * renderScale);

            var output = new SKBitmap(new SKImageInfo(scaledWidth, scaledHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(output);

            canvas.Clear(SKColors.Transparent);

            using var fillPaint = new SKPaint
            {
                Color = canvasFillColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            var hasFill = canvasFillColor.Alpha > 0;
            var fillArea = new SKRect(0, 0, scaledWidth, scaledHeight);

            // 铺满画布的背景色需在裁剪前绘制，否则蒙版外区域无色
            if (hasFill && fillCanvasOutsideMask)
            {
                canvas.DrawRect(fillArea, fillPaint);
            }

            using var path = CreateMaskPath(
                shape,
                scaledWidth,
                scaledHeight,
                scaledCornerRadius,
                circleFitMode,
                circlePaddingPercent);

            if (path != null)
            {
                canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
            }

            // 仅填充蒙版内部时，在应用裁剪路径后绘制底色。
            if (hasFill && !fillCanvasOutsideMask)
            {
                canvas.DrawRect(fillArea, fillPaint);
            }

            var applyTransform = !transformOptions.IsIdentity;
            if (applyTransform)
            {
                canvas.Save();
                ApplyTransform(canvas, scaledWidth, scaledHeight, transformOptions, renderScale);
            }

            using var drawPaint = new SKPaint
            {
                IsAntialias = true
            };
            using var sourceImage = SKImage.FromBitmap(sourceBitmap);

            canvas.DrawImage(
                sourceImage,
                new SKRect(scaledExpand, scaledExpand, scaledExpand + scaledSourceWidth, scaledExpand + scaledSourceHeight),
                HighQualitySampling,
                drawPaint);

            if (applyTransform)
            {
                canvas.Restore();
            }

            canvas.Flush();
            return output;
        }

        private static SKBitmap DownsampleBitmap(SKBitmap source, int targetWidth, int targetHeight)
        {
            var output = new SKBitmap(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(output);
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint
            {
                IsAntialias = true
            };
            using var sourceImage = SKImage.FromBitmap(source);

            canvas.DrawImage(sourceImage, new SKRect(0, 0, targetWidth, targetHeight), HighQualitySampling, paint);
            canvas.Flush();
            return output;
        }

        private static BitmapSource CreateBitmapSource(SKBitmap bitmap, double dpiX, double dpiY)
        {
            var info = bitmap.Info;
            var rowBytes = info.RowBytes;
            var bytes = new byte[rowBytes * info.Height];
            Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);

            var result = BitmapSource.Create(
                info.Width,
                info.Height,
                dpiX,
                dpiY,
                PixelFormats.Pbgra32,
                null,
                bytes,
                rowBytes);

            if (result.CanFreeze)
            {
                result.Freeze();
            }

            return result;
        }

        private static SKPath? CreateMaskPath(
            MaskShape shape,
            int width,
            int height,
            int cornerRadius,
            CircleFitMode circleFitMode,
            int circlePaddingPercent)
        {
            switch (shape)
            {
                case MaskShape.RoundedRectangle:
                    var clampedRadius = Math.Min(cornerRadius, Math.Min(width, height) / 2);
                    if (clampedRadius <= 0)
                    {
                        return null;
                    }

                    var rect = new SKRect(0, 0, width, height);
                    var roundRect = new SKRoundRect(rect, clampedRadius, clampedRadius);
                    var roundedPath = new SKPath();
                    roundedPath.AddRoundRect(roundRect);
                    return roundedPath;

                case MaskShape.Circle:
                    var radius = ResolveCircleRadius(width, height, circleFitMode, circlePaddingPercent);
                    if (radius <= 0)
                    {
                        return null;
                    }
                    var circlePath = new SKPath();
                    circlePath.AddCircle(width / 2f, height / 2f, radius);
                    return circlePath;

                default:
                    return null;
            }
        }

        private static float ResolveCircleRadius(int width, int height, CircleFitMode fitMode, int paddingPercent)
        {
            var minRadius = Math.Min(width, height) / 2f;
            var maxRadius = Math.Max(width, height) / 2f;

            return fitMode switch
            {
                CircleFitMode.Circumscribed => maxRadius,
                CircleFitMode.Padded => minRadius * (1f - Math.Clamp(paddingPercent, 0, 45) / 100f),
                _ => minRadius
            };
        }

        private static void ApplyTransform(SKCanvas canvas, int width, int height, ImageTransformOptions transformOptions, int renderScale)
        {
            var scale = (float)Math.Max(0.01, transformOptions.Scale);
            var offsetX = (float)transformOptions.OffsetX * renderScale;
            var offsetY = (float)transformOptions.OffsetY * renderScale;

            if (Math.Abs(scale - 1f) > 0.0001f)
            {
                canvas.Scale(scale, scale, width / 2f, height / 2f);
            }

            if (Math.Abs(offsetX) > 0.01f || Math.Abs(offsetY) > 0.01f)
            {
                canvas.Translate(offsetX, offsetY);
            }
        }

        private static BitmapSource EnsurePbgra32(BitmapSource source)
        {
            if (source.Format == PixelFormats.Pbgra32)
            {
                return source;
            }

            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Pbgra32;
            converted.EndInit();

            if (converted.CanFreeze)
            {
                converted.Freeze();
            }

            return converted;
        }
    }
}
