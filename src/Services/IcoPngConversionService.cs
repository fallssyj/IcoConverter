using IcoConverter.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace IcoConverter.Services
{
    /// <summary>
    /// ICO 转 PNG 服务实现。
    /// </summary>
    public class IcoPngConversionService : IIcoPngConversionService
    {
        /// <summary>
        /// 读取 ICO 文件中的所有分辨率。
        /// </summary>
        public IReadOnlyList<IcoResolution> GetResolutions(string icoPath)
        {
            if (string.IsNullOrWhiteSpace(icoPath))
            {
                throw new ArgumentException("ICO 路径不能为空。", nameof(icoPath));
            }

            if (!File.Exists(icoPath))
            {
                throw new FileNotFoundException("未找到 ICO 文件。", icoPath);
            }

            var entries = ReadEntries(icoPath);
            return entries
                .Select(entry => new IcoResolution(entry.Width, entry.Height, true))
                .GroupBy(r => (r.Width, r.Height))
                .Select(g => g.First())
                .OrderBy(r => r.Width)
                .ThenBy(r => r.Height)
                .ToList();
        }

        /// <summary>
        /// 将 ICO 中选定的图像导出为 PNG。
        /// </summary>
        public async Task ExportToPngAsync(string icoPath, string outputDirectory, IEnumerable<IcoResolution> selectedResolutions, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(icoPath))
            {
                throw new ArgumentException("ICO 路径不能为空。", nameof(icoPath));
            }

            if (!File.Exists(icoPath))
            {
                throw new FileNotFoundException("未找到 ICO 文件。", icoPath);
            }

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
                throw new InvalidOperationException("请至少选择一个分辨率。");
            }

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                Directory.CreateDirectory(outputDirectory);

                using var stream = new FileStream(icoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream);
                var entries = ReadEntries(reader);

                // 使用字典存放目录项，遇到重复尺寸时保留第一个。
                var entryMap = new Dictionary<(int Width, int Height), IcoEntry>();
                foreach (var entry in entries)
                {
                    entryMap.TryAdd((entry.Width, entry.Height), entry);
                }

                var missing = selectedList
                    .Where(r => !entryMap.ContainsKey((r.Width, r.Height)))
                    .Select(r => $"{r.Width}x{r.Height}")
                    .ToList();

                if (missing.Count > 0)
                {
                    throw new InvalidOperationException($"ICO 中未找到以下分辨率: {string.Join(", ", missing)}");
                }

                var baseName = Path.GetFileNameWithoutExtension(icoPath);
                foreach (var resolution in selectedList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = entryMap[(resolution.Width, resolution.Height)];
                    var imageData = ReadImageData(stream, reader, entry);
                    using var bitmap = CreateBitmapFromEntry(entry, imageData);
                    var outputPath = Path.Combine(outputDirectory, $"{baseName}_{resolution.Width}x{resolution.Height}.png");
                    bitmap.Save(outputPath, ImageFormat.Png);
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 读取 ICO 文件目录项。
        /// </summary>
        private static List<IcoEntry> ReadEntries(string icoPath)
        {
            using var stream = new FileStream(icoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            return ReadEntries(reader);
        }

        /// <summary>
        /// 从二进制读取器读取 ICO 目录项（读取器需位于文件起始位置）。
        /// </summary>
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

            return entries;
        }

        /// <summary>
        /// 读取目录项对应的图像数据。
        /// </summary>
        private static byte[] ReadImageData(Stream stream, BinaryReader reader, IcoEntry entry)
        {
            stream.Seek(entry.ImageOffset, SeekOrigin.Begin);
            if (entry.BytesInRes > int.MaxValue)
            {
                throw new InvalidOperationException("ICO 图像数据过大，无法读取。");
            }

            return reader.ReadBytes((int)entry.BytesInRes);
        }

        /// <summary>
        /// 使用单独的目录项构造临时 ICO 并输出 Bitmap。
        /// </summary>
        private static Bitmap CreateBitmapFromEntry(IcoEntry entry, byte[] imageData)
        {
            if (imageData.Length == 0)
            {
                throw new InvalidOperationException("ICO 图像数据为空。");
            }

            // 某些 PNG 目录项的位平面与位深为 0，需提供安全的回退值。
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

        /// <summary>
        /// ICO 目录项数据结构。
        /// </summary>
        private sealed record IcoEntry(int Width, int Height, ushort Planes, ushort BitCount, uint BytesInRes, uint ImageOffset);
    }
}
