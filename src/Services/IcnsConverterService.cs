using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    /// <summary>
    /// 写出 macOS 的 .icns。文件结构是 4 字节魔数 + 4 字节总长（都是大端），
    /// 后面跟着一串「4 字节类型 + 4 字节长度 + 数据」的元素。
    /// 图像元素统一使用 PNG 载荷。
    /// </summary>
    public sealed class IcnsConverterService : IIcnsConverterService
    {
        private const int ElementHeaderSize = 8;

        /// <summary>
        /// 每个档位指定对应像素尺寸和 ICNS 类型标识。
        /// RetinaType 使用同一份 PNG 数据，以 @2x 类型表示对应的点尺寸。
        /// </summary>
        private static readonly IcnsSlot[] Slots =
        {
            new(16, "icp4", null),
            new(32, "icp5", "ic11"),   // 16pt @2x
            new(64, "icp6", "ic12"),   // 32pt @2x
            new(128, "ic07", null),
            new(256, "ic08", "ic13"),  // 128pt @2x
            new(512, "ic09", "ic14"),  // 256pt @2x
            new(1024, "ic10", null),
        };

        public static IReadOnlyList<int> SupportedSizes { get; } = Slots.Select(s => s.Size).ToArray();

        public static bool IsSupportedSize(int size) => Slots.Any(s => s.Size == size);

        public async Task<IcnsConversionResult> ConvertToIcnsAsync(
            BitmapSource image,
            string outputPath,
            IReadOnlyList<Size> resolutions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(outputPath);

            if (resolutions == null || resolutions.Count == 0)
            {
                throw new ArgumentException("至少需要选择一个尺寸", nameof(resolutions));
            }

            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bytes = BuildIcnsBytes(image, resolutions, cancellationToken, out var written, out var skipped, out var entryCount);

                    // 先写入临时文件再替换，避免写入中断后留下损坏的目标文件
                    var tempPath = outputPath + ".tmp";
                    try
                    {
                        File.WriteAllBytes(tempPath, bytes);
                        File.Move(tempPath, outputPath, overwrite: true);
                    }
                    catch
                    {
                        TryDelete(tempPath);
                        throw;
                    }

                    return new IcnsConversionResult(written, skipped, entryCount, bytes.Length);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"转换到 ICNS 时出错: {ex.Message}", ex);
            }
        }

        private static byte[] BuildIcnsBytes(
            BitmapSource source,
            IReadOnlyList<Size> resolutions,
            CancellationToken cancellationToken,
            out List<Size> writtenSizes,
            out List<Size> skippedSizes,
            out int entryCount)
        {
            writtenSizes = new List<Size>();
            skippedSizes = new List<Size>();
            entryCount = 0;

            var requested = resolutions
                .Where(size => size.Width > 0 && size.Height > 0)
                .Select(size => Math.Max(size.Width, size.Height))
                .Distinct()
                .OrderBy(size => size)
                .ToList();

            var originalBitmap = BitmapExportHelper.ToBitmap(source);
            Bitmap? squareBitmap = null;

            try
            {
                squareBitmap = originalBitmap.Width == originalBitmap.Height
                    ? originalBitmap
                    : BitmapExportHelper.FitToSquare(originalBitmap);

                var elements = new List<(string Type, byte[] Data)>();

                foreach (var slot in Slots)
                {
                    if (!requested.Contains(slot.Size))
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    using var resized = BitmapExportHelper.Resize(squareBitmap, slot.Size, slot.Size);
                    // 各档从同一张方形位图缩放，避免连续缩放造成模糊。
                    var pngData = BitmapExportHelper.EncodePng(resized);

                    elements.Add((slot.PrimaryType, pngData));
                    if (!string.IsNullOrEmpty(slot.RetinaType))
                    {
                        elements.Add((slot.RetinaType!, pngData));
                    }

                    writtenSizes.Add(new Size(slot.Size, slot.Size));
                }

                foreach (var size in requested.Where(size => !IsSupportedSize(size)))
                {
                    skippedSizes.Add(new Size(size, size));
                }

                if (elements.Count == 0)
                {
                    throw new InvalidOperationException(
                        "所选尺寸中没有 ICNS 支持的档位。ICNS 只认 16/32/64/128/256/512/1024。");
                }

                entryCount = elements.Count;
                return Assemble(elements);
            }
            finally
            {
                if (squareBitmap != null && !ReferenceEquals(squareBitmap, originalBitmap))
                {
                    squareBitmap.Dispose();
                }

                originalBitmap.Dispose();
            }
        }

        private static byte[] Assemble(List<(string Type, byte[] Data)> elements)
        {
            using var ms = new MemoryStream();

            // 容器和元素的长度字段均按大端序写入。
            ms.Write(Encoding.ASCII.GetBytes("icns"), 0, 4);
            ms.Write(new byte[4], 0, 4); // 总长度占位，写入完成后回填

            foreach (var element in elements)
            {
                var typeBytes = Encoding.ASCII.GetBytes(element.Type);
                if (typeBytes.Length != 4)
                {
                    throw new InvalidOperationException($"非法的 ICNS 类型标识: {element.Type}");
                }

                ms.Write(typeBytes, 0, 4);
                WriteBigEndian(ms, (uint)(ElementHeaderSize + element.Data.Length));
                ms.Write(element.Data, 0, element.Data.Length);
            }

            var totalLength = (uint)ms.Length;
            ms.Position = 4;
            WriteBigEndian(ms, totalLength);

            return ms.ToArray();
        }

        private static void WriteBigEndian(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private readonly record struct IcnsSlot(int Size, string PrimaryType, string? RetinaType);
    }
}
