using IcoConverter.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace IcoConverter.Services
{
    public class IcoConverterService : IIcoConverterService
    {
        public Task ConvertToIcoAsync(
            BitmapSource image,
            string outputPath,
            List<Size> resolutions,
            CancellationToken cancellationToken = default,
            IcoPngFrameMode frameMode = IcoPngFrameMode.Auto) =>
            ConvertToIcoCoreAsync(image, outputPath, resolutions, cancellationToken, frameMode, overwrite: true);

        public Task<string> ConvertToUniqueIcoAsync(
            BitmapSource image,
            string outputPath,
            List<Size> resolutions,
            CancellationToken cancellationToken = default) =>
            ConvertToIcoCoreAsync(image, outputPath, resolutions, cancellationToken, IcoPngFrameMode.Auto, overwrite: false);

        private async Task<string> ConvertToIcoCoreAsync(
            BitmapSource image,
            string outputPath,
            List<Size> resolutions,
            CancellationToken cancellationToken,
            IcoPngFrameMode frameMode,
            bool overwrite)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(outputPath);

            if (resolutions == null || resolutions.Count == 0)
                throw new ArgumentException("至少需要选择一个尺寸", nameof(resolutions));

            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var icon = CreateIconFromBitmapSource(image, resolutions, cancellationToken, frameMode);
                    cancellationToken.ThrowIfCancellationRequested();

                    // 在目标目录中使用独立的临时文件，完成编码后再移动。
                    var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
                    var tempPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
                    var tempCreated = false;
                    try
                    {
                        using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            tempCreated = true;
                            icon.Save(stream);
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        if (!overwrite)
                        {
                            return MoveToAvailablePath(tempPath, outputPath, cancellationToken);
                        }

                        File.Move(tempPath, outputPath, overwrite: true);
                        return outputPath;
                    }
                    catch
                    {
                        try
                        {
                            if (tempCreated)
                            {
                                File.Delete(tempPath);
                            }
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }

                        throw;
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"转换到ICO时出错: {ex.Message}", ex);
            }
        }

        private static string MoveToAvailablePath(string tempPath, string outputPath, CancellationToken cancellationToken)
        {
            var candidate = outputPath;
            for (var suffix = 2; ; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    File.Move(tempPath, candidate, overwrite: false);
                    return candidate;
                }
                catch (Exception ex) when (
                    (ex is IOException or UnauthorizedAccessException)
                    && (File.Exists(candidate) || Directory.Exists(candidate)))
                {
                    var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
                    var baseName = Path.GetFileNameWithoutExtension(outputPath);
                    var extension = Path.GetExtension(outputPath);
                    candidate = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
                }
            }
        }

        // 组装 ICO 容器后交由 System.Drawing.Icon 处理。
#pragma warning disable CS8600
        private System.Drawing.Icon CreateIconFromBitmapSource(BitmapSource source, List<Size> resolutions, System.Threading.CancellationToken cancellationToken = default, IcoPngFrameMode frameMode = IcoPngFrameMode.Auto)
        {
            ArgumentNullException.ThrowIfNull(source);
            var originalBitmap = BitmapExportHelper.ToBitmap(source);
            var squareBitmap = originalBitmap.Width == originalBitmap.Height
                ? originalBitmap
                : BitmapExportHelper.FitToSquare(originalBitmap);

            try
            {
                var imagePayloads = new List<IconImagePayload>();

                foreach (var resolution in resolutions.OrderBy(r => r.Width))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    System.Drawing.Bitmap resized = null;
                    try
                    {
                        // 各帧从同一张方形位图缩放，避免连续缩放造成模糊。
                        resized = BitmapExportHelper.Resize(squareBitmap, resolution.Width, resolution.Height);
                        var encodeAsPng = ShouldEncodeAsPng(resolution.Width, resolution.Height, frameMode);
                        var data = encodeAsPng ? BitmapExportHelper.EncodePng(resized!) : GetBitmapDibData(resized!);
                        if (data == null || data.Length == 0)
                        {
                            throw new InvalidOperationException($"调整到 {resolution.Width}x{resolution.Height} 时编码失败: DIB 数据为空");
                        }

                        imagePayloads.Add(new IconImagePayload(resized!.Width, resized!.Height, data, encodeAsPng));
                    }
                    catch (Exception ex)
                    {
                        resized?.Dispose();
                        throw new InvalidOperationException($"调整到 {resolution.Width}x{resolution.Height} 时出错: {ex.Message}", ex);
                    }
                    finally
                    {
                        resized?.Dispose();
                    }
                }

                using var ms = new MemoryStream();
                WriteIcoHeader(ms, imagePayloads.Count);

                // 数据区紧跟目录，偏移 = 6 字节文件头 + 每项 16 字节
                int currentOffset = 6 + (imagePayloads.Count * 16);

                for (int i = 0; i < imagePayloads.Count; i++)
                {
                    var payload = imagePayloads[i];
                    var entry = CreateDirectoryEntry(payload.Width, payload.Height, payload.Data.Length, currentOffset, payload.IsPng);
                    ms.Write(entry, 0, entry.Length);
                    currentOffset += payload.Data.Length;
                }

                // 按相同顺序写入数据区
                for (int i = 0; i < imagePayloads.Count; i++)
                {
                    var payload = imagePayloads[i];
                    ms.Write(payload.Data, 0, payload.Data.Length);
                }

                // 为 Icon 创建独立的输入流，以便释放组装容器时使用的流。
                var iconBytes = ms.ToArray();
                return new System.Drawing.Icon(new MemoryStream(iconBytes));
            }
            finally
            {
                squareBitmap?.Dispose();
                if (!ReferenceEquals(squareBitmap, originalBitmap))
                {
                    originalBitmap?.Dispose();
                }
            }
        }
#pragma warning restore CS8600

        private static void WriteIcoHeader(Stream stream, int imageCount)
        {
            // 保留 2 字节 0 + 类型 2 字节（1 = ICO）+ 图像数量 2 字节
            stream.Write([0, 0, 1, 0], 0, 4);
            stream.WriteByte((byte)(imageCount & 0xFF));
            stream.WriteByte((byte)((imageCount >> 8) & 0xFF));
        }

        private static byte[] CreateDirectoryEntry(int widthPx, int heightPx, int dataSize, int offset, bool isPng)
        {
            using var ms = new MemoryStream();
            // 目录项中的 0 代表 256。更大的扩展尺寸也写入 0，读取器需从帧数据取得实际宽高。
            byte width = (byte)(widthPx >= 256 ? 0 : widthPx);
            ms.WriteByte(width);

            byte height = (byte)(heightPx >= 256 ? 0 : heightPx);
            ms.WriteByte(height);

            ms.WriteByte(0);
            ms.WriteByte(0);

            ushort planes = isPng ? (ushort)0 : (ushort)1;
            ms.WriteByte((byte)(planes & 0xFF));
            ms.WriteByte((byte)((planes >> 8) & 0xFF));

            ushort bitCount = isPng ? (ushort)0 : (ushort)32;
            ms.WriteByte((byte)(bitCount & 0xFF));
            ms.WriteByte((byte)((bitCount >> 8) & 0xFF));

            ms.WriteByte((byte)(dataSize & 0xFF));
            ms.WriteByte((byte)((dataSize >> 8) & 0xFF));
            ms.WriteByte((byte)((dataSize >> 16) & 0xFF));
            ms.WriteByte((byte)((dataSize >> 24) & 0xFF));

            ms.WriteByte((byte)(offset & 0xFF));
            ms.WriteByte((byte)((offset >> 8) & 0xFF));
            ms.WriteByte((byte)((offset >> 16) & 0xFF));
            ms.WriteByte((byte)((offset >> 24) & 0xFF));

            return ms.ToArray();
        }

        // ICO 里放的是裸 DIB，没有 BMP 文件头；高度要写两倍，因为后面还跟着 AND mask
        private static byte[] GetBitmapDibData(System.Drawing.Bitmap bitmap)
        {
            using var converted = BitmapExportHelper.Ensure32bppArgb(bitmap);

            int width = converted.Width;
            int height = converted.Height;
            int stride = width * 4;
            int maskStride = ((width + 31) / 32) * 4;

            var pixels = new byte[stride * height];
            var data = converted.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                DrawingPixelFormat.Format32bppArgb);

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                converted.UnlockBits(data);
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write(40);                // biSize
            bw.Write(width);             // biWidth
            bw.Write(height * 2);        // biHeight（包含 AND mask）
            bw.Write((short)1);          // biPlanes
            bw.Write((short)32);         // biBitCount
            bw.Write(0);                 // biCompression (BI_RGB)
            bw.Write(stride * height + maskStride * height); // biSizeImage
            bw.Write(0);                 // biXPelsPerMeter
            bw.Write(0);                 // biYPelsPerMeter
            bw.Write(0);                 // biClrUsed
            bw.Write(0);                 // biClrImportant

            // DIB 采用自底向上的行序，需反向写入
            for (int y = height - 1; y >= 0; y--)
            {
                bw.Write(pixels, y * stride, stride);
            }

            // 32bpp 含 alpha 时 AND mask 可全为 0，保留该区域仅为兼容旧渲染路径
            var mask = new byte[maskStride * height];
            bw.Write(mask);

            return ms.ToArray();
        }

        private static bool ShouldEncodeAsPng(int width, int height, IcoPngFrameMode frameMode)
        {
            switch (frameMode)
            {
                case IcoPngFrameMode.AlwaysPng:
                    return true;
                case IcoPngFrameMode.NeverPng:
                    return false;
                default:
                    // 自动模式在长边达到 256 像素时使用 PNG。较小的帧用 DIB 兼容旧版读取器。
                    return Math.Max(width, height) >= 256;
            }
        }

        private sealed record IconImagePayload(int Width, int Height, byte[] Data, bool IsPng);

    }
}
