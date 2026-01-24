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
        /// <summary>
        /// 对提供的 <see cref="BitmapSource"/> 按指定形状裁剪并返回一个新的 <see cref="BitmapSource"/>。
        /// 返回的图像与原图保持相同的尺寸与 DPI，使用 SkiaSharp 进行像素级裁剪并冻结结果，
        /// 以确保可安全在多个线程间使用。
        /// </summary>
        public BitmapSource ApplyMask(BitmapSource source, MaskShape shape, int cornerRadius, int polygonSides, double polygonRotationDegrees)
        {
            var normalizedSource = EnsurePbgra32(source);
            var width = normalizedSource.PixelWidth;
            var height = normalizedSource.PixelHeight;
            using var maskPath = CreateMaskPath(shape, width, height, cornerRadius, polygonSides, polygonRotationDegrees);
            if (maskPath == null)
            {
                return source;
            }

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var pixelBuffer = new byte[info.BytesSize];
            var handle = GCHandle.Alloc(pixelBuffer, GCHandleType.Pinned);
            BitmapSource result;
            try
            {
                normalizedSource.CopyPixels(new Int32Rect(0, 0, width, height), handle.AddrOfPinnedObject(), pixelBuffer.Length, info.RowBytes);

                using var tempBitmap = new SKBitmap();
                if (!tempBitmap.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes))
                {
                    throw new InvalidOperationException("无法初始化 SkiaSharp 位图。");
                }

                using var surface = SKSurface.Create(info);
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                canvas.ClipPath(maskPath, SKClipOperation.Intersect, antialias: true);
                canvas.DrawBitmap(tempBitmap, 0, 0);
                canvas.Flush();

                using var rounded = surface.Snapshot();
                var rowBytes = info.RowBytes;
                var buffer = new byte[rowBytes * height];
                var outputHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    if (!rounded.ReadPixels(info, outputHandle.AddrOfPinnedObject(), rowBytes, 0, 0))
                    {
                        throw new InvalidOperationException("无法读取 SkiaSharp 渲染结果。");
                    }
                }
                finally
                {
                    outputHandle.Free();
                }

                result = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Pbgra32, null, buffer, rowBytes);
            }
            finally
            {
                handle.Free();
            }
            if (result.CanFreeze)
            {
                result.Freeze();
            }

            return result;
        }

        private static SKPath? CreateMaskPath(MaskShape shape, int width, int height, int cornerRadius, int polygonSides, double polygonRotationDegrees)
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
                    var radius = cornerRadius <= 0 ? Math.Min(width, height) / 2f : Math.Min(cornerRadius, Math.Min(width, height) / 2f);
                    if (radius <= 0)
                    {
                        return null;
                    }
                    var circlePath = new SKPath();
                    circlePath.AddCircle(width / 2f, height / 2f, radius);
                    return circlePath;

                case MaskShape.Ellipse:
                    var ellipsePath = new SKPath();
                    ellipsePath.AddOval(new SKRect(0, 0, width, height));
                    return ellipsePath;

                case MaskShape.Polygon:
                    var sides = Math.Max(3, polygonSides);
                    return CreatePolygonPath(width, height, sides, polygonRotationDegrees);

                default:
                    return null;
            }
        }

        private static SKPath? CreatePolygonPath(int width, int height, int sides, double rotationDegrees)
        {
            var path = new SKPath { FillType = SKPathFillType.Winding };
            var center = new SKPoint(width / 2f, height / 2f);
            var radius = Math.Min(width, height) / 2f;
            var angleStep = (float)(2 * Math.PI / sides);
            var startAngle = -Math.PI / 2f + rotationDegrees * Math.PI / 180.0;

            for (int i = 0; i < sides; i++)
            {
                var angle = startAngle + i * angleStep;
                var x = center.X + radius * (float)Math.Cos(angle);
                var y = center.Y + radius * (float)Math.Sin(angle);
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            path.Close();
            return path;
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
