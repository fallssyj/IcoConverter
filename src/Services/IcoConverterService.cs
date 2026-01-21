using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public class IcoConverterService : IIcoConverterService
    {
        /// <summary>
        /// 异步地将 WPF 的 <see cref="BitmapSource"/> 转换为 ICO 文件，ICO 中包含按 <paramref name="resolutions"/> 指定的
        /// BMP/DIB 图像，并将结果保存到 <paramref name="outputPath"/>。
        /// </summary>
        public async System.Threading.Tasks.Task ConvertToIcoAsync(BitmapSource image, string outputPath, List<Size> resolutions, System.Threading.CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(outputPath);

            if (resolutions == null || resolutions.Count == 0)
                throw new ArgumentException("至少需要选择一个分辨率", nameof(resolutions));

            try
            {
                // 执行 CPU/IO 密集型工作在线程池线程上
                await System.Threading.Tasks.Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var icon = CreateIconFromBitmapSource(image, resolutions, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = new FileStream(outputPath, FileMode.Create);
                    icon.Save(stream);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 传递取消异常给调用者
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"转换到ICO时出错: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从给定的可执行文件中提取关联图标并保存成 ICO 文件。
        /// </summary>
        public async System.Threading.Tasks.Task ExtractIconFromExecutableAsync(string executablePath, string outputPath, System.Threading.CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(executablePath);
            ArgumentException.ThrowIfNullOrEmpty(outputPath);

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException("未找到可执行文件", executablePath);
            }

            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var flags = NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE | NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_IMAGE_RESOURCE;
                    var moduleHandle = NativeMethods.LoadLibraryEx(executablePath, IntPtr.Zero, flags);
                    if (moduleHandle == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载可执行文件资源。");
                    }

                    try
                    {
                        var candidates = new List<(int Score, byte[] Data)>();
                        bool enumerationResult = NativeMethods.EnumResourceNames(
                            moduleHandle,
                            new IntPtr(NativeMethods.RT_GROUP_ICON),
                            (hModule, type, name, lParam) =>
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                var entries = IconResourceExtractor.ReadGroupEntries(hModule, type, name);
                                var icoBytes = IconResourceExtractor.BuildIcoFromEntries(hModule, entries);
                                var score = IconResourceExtractor.CalculateQualityScore(entries);

                                candidates.Add((score, icoBytes));
                                return true;
                            },
                            IntPtr.Zero);

                        var enumerationError = Marshal.GetLastWin32Error();
                        if (!enumerationResult && enumerationError != 0 && candidates.Count == 0)
                        {
                            throw new Win32Exception(enumerationError, "枚举图标资源失败。");
                        }

                        if (candidates.Count == 0)
                        {
                            throw new InvalidOperationException("未在可执行文件中找到图标资源。");
                        }

                        var bestCandidate = candidates
                            .OrderByDescending(candidate => candidate.Score)
                            .First();

                        cancellationToken.ThrowIfCancellationRequested();
                        File.WriteAllBytes(outputPath, bestCandidate.Data);
                    }
                    finally
                    {
                        NativeMethods.FreeLibrary(moduleHandle);
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"提取可执行文件图标时出错: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 从给定的 <see cref="BitmapSource"/> 为指定的 <paramref name="resolutions"/> 生成包含 BMP/DIB 数据的
        /// <see cref="System.Drawing.Icon"/>。方法对每个尺寸进行调整并以 DIB 编码，最后在内存中写入 ICO 流。
        /// </summary>
#pragma warning disable CS8600
        private System.Drawing.Icon CreateIconFromBitmapSource(BitmapSource source, List<Size> resolutions, System.Threading.CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            var originalBitmap = BitmapSourceToBitmap(source);

            try
            {
                // 为每个分辨率生成 DIB 数据（一次编码，避免重复编码开销），并记录尺寸
                var sizes = new List<System.Drawing.Size>();
                var bitmapDatas = new List<byte[]>();

                foreach (var resolution in resolutions.OrderBy(r => r.Width))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    System.Drawing.Bitmap resized = null;
                    try
                    {
                        resized = ResizeBitmap(originalBitmap, resolution.Width, resolution.Height);
                        var data = GetBitmapDibData(resized!);
                        if (data == null || data.Length == 0)
                        {
                            throw new InvalidOperationException($"调整到 {resolution.Width}x{resolution.Height} 时编码失败: DIB 数据为空");
                        }

                        sizes.Add(new System.Drawing.Size(resized!.Width, resized!.Height));
                        bitmapDatas.Add(data);
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
                WriteIcoHeader(ms, bitmapDatas.Count);

                int currentOffset = 6 + (bitmapDatas.Count * 16);

                // 写入目录条目（使用已编码的数据长度）
                for (int i = 0; i < bitmapDatas.Count; i++)
                {
                    var entry = CreateDirectoryEntry(sizes[i].Width, sizes[i].Height, bitmapDatas[i].Length, currentOffset);
                    ms.Write(entry, 0, entry.Length);
                    currentOffset += bitmapDatas[i].Length;
                }

                // 写入位图（DIB）数据
                for (int i = 0; i < bitmapDatas.Count; i++)
                {
                    ms.Write(bitmapDatas[i], 0, bitmapDatas[i].Length);
                }

                // 返回图标对象（从内存复制字节以避免对原流的依赖）
                var iconBytes = ms.ToArray();
                return new System.Drawing.Icon(new MemoryStream(iconBytes));
            }
            finally
            {
                originalBitmap?.Dispose();
            }
        }
#pragma warning restore CS8600

        /// <summary>
        /// 为 <paramref name="imageCount"/> 张图像写入 ICO 头（iconDir）。
        /// </summary>
        private static void WriteIcoHeader(Stream stream, int imageCount)
        {
            // ICO文件头: 2字节保留(0) + 2字节类型(1=ICO) + 2字节图像数量
            stream.Write([0, 0, 1, 0], 0, 4);
            stream.WriteByte((byte)(imageCount & 0xFF));
            stream.WriteByte((byte)((imageCount >> 8) & 0xFF));
        }

        /// <summary>
        /// 创建 ICO 目录项的 16 字节条目。
        /// </summary>
        private static byte[] CreateDirectoryEntry(int widthPx, int heightPx, int dataSize, int offset)
        {
            using var ms = new MemoryStream();
            byte width = (byte)(widthPx >= 256 ? 0 : widthPx);
            ms.WriteByte(width);

            byte height = (byte)(heightPx >= 256 ? 0 : heightPx);
            ms.WriteByte(height);

            ms.WriteByte(0);
            ms.WriteByte(0);

            ms.Write([1, 0], 0, 2);
            ms.Write([32, 0], 0, 2);

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

        /// <summary>
        /// 将提供的 <see cref="System.Drawing.Bitmap"/> 编码为 ICO 所需的 BMP/DIB 数据并返回字节数组。
        /// 注意：ICO 中存储的是 DIB（不包含 BMP 文件头），并且高度需要写成原高度的 2 倍（包含 AND mask）。
        /// </summary>
        private static byte[] GetBitmapDibData(System.Drawing.Bitmap bitmap)
        {
            // ICO 约定使用 32bpp BGRA + 1bpp AND mask
            using var converted = Ensure32bppArgb(bitmap);

            int width = converted.Width;
            int height = converted.Height;
            int stride = width * 4;
            int maskStride = ((width + 31) / 32) * 4;

            var pixels = new byte[stride * height];
            var data = converted.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                converted.UnlockBits(data);
            }

            // 构造 DIB：BITMAPINFOHEADER + XOR + AND mask
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // BITMAPINFOHEADER
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

            // XOR 位图数据（按自底向上顺序写入）
            for (int y = height - 1; y >= 0; y--)
            {
                bw.Write(pixels, y * stride, stride);
            }

            // AND mask（32bpp 带 alpha 时可全 0）
            var mask = new byte[maskStride * height];
            bw.Write(mask);

            return ms.ToArray();
        }

        /// <summary>
        /// 确保位图为 32bpp ARGB，避免格式不一致导致写入错误。
        /// </summary>
        private static System.Drawing.Bitmap Ensure32bppArgb(System.Drawing.Bitmap source)
        {
            if (source.PixelFormat == PixelFormat.Format32bppArgb)
            {
                return (System.Drawing.Bitmap)source.Clone();
            }

            var converted = new System.Drawing.Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using var g = System.Drawing.Graphics.FromImage(converted);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            return converted;
        }

        /// <summary>
        /// 将 WPF 的 <see cref="BitmapSource"/> 的像素复制到 <see cref="System.Drawing.Bitmap"/> 中。
        /// </summary>
        private static System.Drawing.Bitmap BitmapSourceToBitmap(BitmapSource source)
        {
            var bitmap = new System.Drawing.Bitmap(
                source.PixelWidth,
                source.PixelHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            var data = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
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

        /// <summary>
        /// 使用高质量插值设置将提供的 <see cref="System.Drawing.Bitmap"/> 调整到指定尺寸。
        /// </summary>
        private static System.Drawing.Bitmap ResizeBitmap(System.Drawing.Bitmap original, int width, int height)
        {
            var resized = new System.Drawing.Bitmap(width, height, PixelFormat.Format32bppArgb);

            using (var graphics = System.Drawing.Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

                graphics.DrawImage(original, 0, 0, width, height);
            }

            return resized;
        }

        private static class IconResourceExtractor
        {
            internal static IReadOnlyList<GroupIconEntry> ReadGroupEntries(IntPtr moduleHandle, IntPtr resourceType, IntPtr resourceName)
            {
                var bytes = NativeMethods.LoadResourceData(moduleHandle, resourceType, resourceName);
                using var ms = new MemoryStream(bytes);
                using var reader = new BinaryReader(ms);

                var reserved = reader.ReadUInt16();
                var resourceTypeValue = reader.ReadUInt16();
                var count = reader.ReadUInt16();

                if (reserved != 0 || resourceTypeValue != 1)
                {
                    throw new InvalidOperationException("无效的图标组资源。");
                }

                var entries = new List<GroupIconEntry>(count);
                for (int i = 0; i < count; i++)
                {
                    entries.Add(new GroupIconEntry(
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadByte(),
                        reader.ReadUInt16(),
                        reader.ReadUInt16(),
                        reader.ReadUInt32(),
                        reader.ReadUInt16()));
                }

                return entries
                    .OrderByDescending(e => NormalizeDimension(e.Width) * NormalizeDimension(e.Height))
                    .ThenByDescending(e => e.BitCount)
                    .ToArray();
            }

            internal static byte[] BuildIcoFromEntries(IntPtr moduleHandle, IReadOnlyList<GroupIconEntry> entries)
            {
                if (entries == null || entries.Count == 0)
                {
                    throw new InvalidOperationException("图标组为空。");
                }

                var iconData = new List<byte[]>(entries.Count);
                foreach (var entry in entries)
                {
                    var data = NativeMethods.LoadResourceData(moduleHandle, new IntPtr(NativeMethods.RT_ICON), new IntPtr(entry.ResourceId));
                    iconData.Add(data);
                }

                using var ms = new MemoryStream();
                using var writer = new BinaryWriter(ms);

                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)entries.Count);

                int offset = 6 + entries.Count * 16;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var data = iconData[i];

                    writer.Write(entry.Width);
                    writer.Write(entry.Height);
                    writer.Write(entry.ColorCount);
                    writer.Write(entry.Reserved);
                    writer.Write(entry.Planes);
                    writer.Write(entry.BitCount);
                    writer.Write(data.Length);
                    writer.Write(offset);

                    offset += data.Length;
                }

                foreach (var data in iconData)
                {
                    writer.Write(data);
                }

                writer.Flush();
                return ms.ToArray();
            }

            internal static int CalculateQualityScore(IReadOnlyList<GroupIconEntry> entries)
            {
                if (entries == null || entries.Count == 0)
                {
                    return 0;
                }

                int maxArea = entries.Max(e => NormalizeDimension(e.Width) * NormalizeDimension(e.Height));
                int maxBitDepth = entries.Max(e => e.BitCount);
                return (maxArea * 1000) + maxBitDepth;
            }

            private static int NormalizeDimension(byte value) => value == 0 ? 256 : value;
        }

        private readonly struct GroupIconEntry
        {
            internal GroupIconEntry(byte width, byte height, byte colorCount, byte reserved, ushort planes, ushort bitCount, uint bytesInRes, ushort resourceId)
            {
                Width = width;
                Height = height;
                ColorCount = colorCount;
                Reserved = reserved;
                Planes = planes;
                BitCount = bitCount;
                BytesInRes = bytesInRes;
                ResourceId = resourceId;
            }

            internal byte Width { get; }
            internal byte Height { get; }
            internal byte ColorCount { get; }
            internal byte Reserved { get; }
            internal ushort Planes { get; }
            internal ushort BitCount { get; }
            internal uint BytesInRes { get; }
            internal ushort ResourceId { get; }
        }

        private static class NativeMethods
        {
            internal const int RT_ICON = 3;
            internal const int RT_GROUP_ICON = 14;

            [Flags]
            internal enum LoadLibraryFlags : uint
            {
                LOAD_LIBRARY_AS_DATAFILE = 0x00000002,
                LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020
            }

            internal delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, LoadLibraryFlags dwFlags);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            internal static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr LockResource(IntPtr hResData);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

            internal static byte[] LoadResourceData(IntPtr hModule, IntPtr lpType, IntPtr lpName)
            {
                var resourceInfo = FindResource(hModule, lpName, lpType);
                if (resourceInfo == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法定位资源。");
                }

                var size = SizeofResource(hModule, resourceInfo);
                if (size == 0)
                {
                    throw new InvalidOperationException("资源大小为0。");
                }

                var resourceHandle = LoadResource(hModule, resourceInfo);
                if (resourceHandle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载资源。");
                }

                var lockedResource = LockResource(resourceHandle);
                if (lockedResource == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法锁定资源。");
                }

                var length = checked((int)size);
                var buffer = new byte[length];
                Marshal.Copy(lockedResource, buffer, 0, length);
                return buffer;
            }
        }
    }
}
