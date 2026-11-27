using IcoConverter.Models;
using System;
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace IcoConverter.Services
{
    /// <summary>
    /// 按文件内容分派：ICO 走目录项 + DIB/PNG 的解析，ICNS 走元素流。
    /// 两边对外都只是「若干尺寸的帧」，导出时统统落成 PNG。
    /// </summary>
    public class IconPngConversionService : IIconPngConversionService
    {
        public IReadOnlyList<IcoResolution> GetResolutions(string iconPath)
        {
            ValidatePath(iconPath);

            return IsIcns(iconPath)
                ? GetIcnsResolutions(iconPath)
                : GetIcoResolutions(iconPath);
        }

        public async Task ExportToPngAsync(
            string iconPath,
            string outputDirectory,
            IEnumerable<IcoResolution> selectedResolutions,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(iconPath);

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("输出目录不能为空。", nameof(outputDirectory));
            }

            if (selectedResolutions == null)
            {
                throw new ArgumentNullException(nameof(selectedResolutions));
            }

            var selectedList = selectedResolutions
                .Where(r => r.IsSelected)
                .GroupBy(r => (r.Width, r.Height))
                .Select(g => g.First())
                .ToList();
            if (selectedList.Count == 0)
            {
                throw new InvalidOperationException("请至少选择一个尺寸。");
            }

            var isIcns = IsIcns(iconPath);

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(outputDirectory);

                if (isIcns)
                {
                    ExportIcnsToPng(iconPath, outputDirectory, selectedList, cancellationToken);
                }
                else
                {
                    ExportIcoToPng(iconPath, outputDirectory, selectedList, cancellationToken);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private static void ValidatePath(string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                throw new ArgumentException("图标路径不能为空。", nameof(iconPath));
            }

            if (!File.Exists(iconPath))
            {
                throw new FileNotFoundException("未找到图标文件。", iconPath);
            }
        }

        // 先检查文件头魔数，扩展名与实际格式不符时仍可解析
        private static bool IsIcns(string iconPath)
        {
            if (string.Equals(Path.GetExtension(iconPath), ".icns", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IcnsReader.HasIcnsSignature(iconPath);
        }

        #region ICO

        private static IReadOnlyList<IcoResolution> GetIcoResolutions(string icoPath)
        {
            using var stream = new FileStream(icoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);

            var entries = ReadEntries(reader).ToLookup(entry => (entry.DisplayWidth, entry.DisplayHeight));
            return CreateResolutions(entries, entry => DecodeIcoEntry(reader, entry));
        }

        private static void ExportIcoToPng(
            string icoPath,
            string outputDirectory,
            IReadOnlyList<IcoResolution> selectedList,
            CancellationToken cancellationToken)
        {
            using var stream = new FileStream(icoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            var entries = ReadEntries(reader).ToLookup(entry => (entry.DisplayWidth, entry.DisplayHeight));
            ExportFrames(entries, entry => DecodeIcoEntry(reader, entry), icoPath, outputDirectory,
                selectedList, "ICO", cancellationToken);
        }

        // 调用前需将 reader 定位到文件起始位置
        private static List<IcoEntry> ReadEntries(BinaryReader reader)
        {
            var reserved = reader.ReadUInt16();
            var type = reader.ReadUInt16();
            var count = reader.ReadUInt16();

            if (reserved != 0 || type != 1)
            {
                throw new InvalidOperationException("文件格式无效，未识别为 ICO。");
            }

            if (count == 0)
            {
                throw new InvalidOperationException("ICO 文件中没有可用图像。");
            }

            var entries = new List<IcoEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var width = reader.ReadByte();
                var height = reader.ReadByte();
                reader.ReadByte();
                reader.ReadByte();
                var planes = reader.ReadUInt16();
                var bitCount = reader.ReadUInt16();
                var bytesInRes = reader.ReadUInt32();
                var imageOffset = reader.ReadUInt32();

                entries.Add(new IcoEntry(width == 0 ? 256 : width,
                    height == 0 ? 256 : height,
                    planes,
                    bitCount,
                    bytesInRes,
                    imageOffset));
            }

            PopulatePngEntryDimensions(reader.BaseStream, entries);
            return entries;
        }

        private static Bitmap DecodeIcoEntry(BinaryReader reader, IcoEntry entry)
        {
            if (entry.BytesInRes > int.MaxValue)
            {
                throw new InvalidOperationException("ICO 图像数据过大，无法读取。");
            }

            reader.BaseStream.Seek(entry.ImageOffset, SeekOrigin.Begin);
            return CreateBitmapFromEntry(entry, reader.ReadBytes((int)entry.BytesInRes));
        }

        // 从 PNG 帧头读取实际尺寸，以识别目录项中以 0 表示的大尺寸。
        private static void PopulatePngEntryDimensions(Stream stream, List<IcoEntry> entries)
        {
            var originalPosition = stream.Position;
            Span<byte> header = stackalloc byte[24];

            foreach (var entry in entries)
            {
                if (entry.BytesInRes < header.Length)
                {
                    continue;
                }

                stream.Seek(entry.ImageOffset, SeekOrigin.Begin);
                var read = stream.Read(header);
                if (read < header.Length)
                {
                    continue;
                }

                if (!IsPngPayload(header))
                {
                    continue;
                }

                entry.IsPng = true;
                entry.TrueWidth = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
                entry.TrueHeight = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
            }

            stream.Seek(originalPosition, SeekOrigin.Begin);
        }

        // Icon 类不支持直接读取 DIB 条目，需先构造仅含该项的 ICO 头
        private static Bitmap CreateBitmapFromEntry(IcoEntry entry, byte[] imageData)
        {
            if (imageData.Length == 0)
            {
                throw new InvalidOperationException("ICO 图像数据为空。");
            }

            if (entry.IsPng || IsPngPayload(imageData.AsSpan()))
            {
                return DecodePng(imageData);
            }

            // 部分条目的 planes/bitCount 为 0，原样传入会导致 Icon 抛出异常
            var planes = entry.Planes == 0 ? (ushort)1 : entry.Planes;
            var bitCount = entry.BitCount == 0 ? (ushort)32 : entry.BitCount;

            using var iconStream = new MemoryStream();
            using (var writer = new BinaryWriter(iconStream, System.Text.Encoding.Default, true))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)1);
                writer.Write((byte)(entry.Width == 256 ? 0 : entry.Width));
                writer.Write((byte)(entry.Height == 256 ? 0 : entry.Height));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write(planes);
                writer.Write(bitCount);
                writer.Write((uint)imageData.Length);
                writer.Write((uint)(6 + 16));
                writer.Write(imageData);
            }

            iconStream.Position = 0;
            using var icon = new Icon(iconStream);
            return icon.ToBitmap();
        }

        #endregion

        #region ICNS

        private static IReadOnlyList<IcoResolution> GetIcnsResolutions(string icnsPath)
        {
            var frames = IcnsReader.ReadFrames(icnsPath).ToLookup(frame => (frame.Width, frame.Height));
            return CreateResolutions(frames, frame => DecodePng(frame.PngData));
        }

        private static void ExportIcnsToPng(
            string icnsPath,
            string outputDirectory,
            IReadOnlyList<IcoResolution> selectedList,
            CancellationToken cancellationToken)
        {
            var frames = IcnsReader.ReadFrames(icnsPath).ToLookup(frame => (frame.Width, frame.Height));
            ExportFrames(frames, frame => DecodePng(frame.PngData), icnsPath, outputDirectory,
                selectedList, "ICNS", cancellationToken);
        }

        #endregion

        private static Bitmap DecodePng(byte[] imageData)
        {
            // 返回独立位图，避免解码结果依赖已释放的流。
            using var stream = new MemoryStream(imageData, writable: false);
            using var bitmap = new Bitmap(stream);
            return new Bitmap(bitmap);
        }

        private static IReadOnlyList<IcoResolution> CreateResolutions<T>(
            ILookup<(int Width, int Height), T> frames,
            Func<T, Bitmap> decode)
        {
            var resolutions = new List<IcoResolution>();
            foreach (var group in frames.OrderBy(group => group.Key.Width).ThenBy(group => group.Key.Height))
            {
                var resolution = new IcoResolution(group.Key.Width, group.Key.Height, true);
                using var bitmap = DecodeFirstFrame(group, decode, CancellationToken.None, out _);
                if (bitmap != null)
                {
                    try
                    {
                        resolution.Preview = ToBitmapSource(bitmap);
                    }
                    catch (Exception ex) when (IsFrameDecodeError(ex))
                    {
                        // 缩略图生成失败时仍显示该尺寸。
                    }
                }

                resolutions.Add(resolution);
            }

            return resolutions;
        }

        private static void ExportFrames<T>(
            ILookup<(int Width, int Height), T> frames,
            Func<T, Bitmap> decode,
            string iconPath,
            string outputDirectory,
            IReadOnlyList<IcoResolution> selectedList,
            string formatName,
            CancellationToken cancellationToken)
        {
            EnsureAllExist(frames.Select(group => group.Key), selectedList, formatName);
            var baseName = Path.GetFileNameWithoutExtension(iconPath);
            foreach (var resolution in selectedList)
            {
                var size = (resolution.Width, resolution.Height);
                using var bitmap = DecodeFirstFrame(frames[size], decode, cancellationToken, out var decodeError)
                    ?? throw new InvalidOperationException(
                        $"无法解码 {formatName} 的 {size.Width}×{size.Height} 帧。", decodeError);

                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = Path.Combine(outputDirectory, $"{baseName}_{resolution.Width}x{resolution.Height}.png");
                bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        private static Bitmap? DecodeFirstFrame<T>(
            IEnumerable<T> frames,
            Func<T, Bitmap> decode,
            CancellationToken cancellationToken,
            out Exception? decodeError)
        {
            decodeError = null;
            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return decode(frame);
                }
                catch (Exception ex) when (IsFrameDecodeError(ex))
                {
                    decodeError = ex;
                }
            }

            return null;
        }

        // GDI+ 也可能以 OutOfMemoryException 报告无法解码的图像。
        private static bool IsFrameDecodeError(Exception exception) =>
            exception is ArgumentException or IOException or InvalidOperationException
                or ExternalException or OutOfMemoryException;

        /// <summary>将从容器解码的帧转换为 WPF 可显示的位图。BitmapSource.Create 内部复制像素，释放原始流后仍可使用。</summary>
        private static System.Windows.Media.Imaging.BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            var data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var source = System.Windows.Media.Imaging.BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    96,
                    96,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null,
                    data.Scan0,
                    data.Stride * bitmap.Height,
                    data.Stride);

                // 冻结位图以允许跨线程共享。
                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static void EnsureAllExist(
            IEnumerable<(int Width, int Height)> available,
            IReadOnlyList<IcoResolution> selectedList,
            string formatName)
        {
            var availableSet = available.ToHashSet();
            var missing = selectedList
                .Where(r => !availableSet.Contains((r.Width, r.Height)))
                .Select(r => $"{r.Width}x{r.Height}")
                .ToList();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{formatName} 中未找到以下尺寸： {string.Join(", ", missing)}");
            }
        }

        private static bool IsPngPayload(ReadOnlySpan<byte> data)
        {
            if (data.Length < 8)
            {
                return false;
            }

            Span<byte> signature = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            return data.Slice(0, 8).SequenceEqual(signature);
        }

        private sealed class IcoEntry
        {
            public IcoEntry(int width, int height, ushort planes, ushort bitCount, uint bytesInRes, uint imageOffset)
            {
                Width = width;
                Height = height;
                Planes = planes;
                BitCount = bitCount;
                BytesInRes = bytesInRes;
                ImageOffset = imageOffset;
            }

            public int Width { get; }
            public int Height { get; }
            public ushort Planes { get; }
            public ushort BitCount { get; }
            public uint BytesInRes { get; }
            public uint ImageOffset { get; }
            public bool IsPng { get; set; }
            public int? TrueWidth { get; set; }
            public int? TrueHeight { get; set; }
            public int DisplayWidth => TrueWidth ?? Width;
            public int DisplayHeight => TrueHeight ?? Height;
        }
    }
}
