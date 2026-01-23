using IcoConverter.Models;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Text;
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
        /// 扫描可执行文件或 DLL，并返回其中包含的所有图标组合。
        /// </summary>
        public async System.Threading.Tasks.Task<IReadOnlyList<ExecutableIconCandidate>> ScanExecutableIconsAsync(string binaryPath, System.Threading.CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(binaryPath);

            if (!File.Exists(binaryPath))
            {
                throw new FileNotFoundException("未找到可执行文件", binaryPath);
            }

            var probePaths = BuildResourceProbeList(binaryPath);
            Exception? lastError = null;

            foreach (var path in probePaths)
            {
                try
                {
                    var icons = await System.Threading.Tasks.Task.Run(() => ExtractIconsFromFile(path, cancellationToken), cancellationToken).ConfigureAwait(false);
                    if (icons.Count > 0)
                    {
                        return icons;
                    }

                    lastError = new InvalidOperationException($"在文件 '{path}' 中未找到图标资源。");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            throw lastError ?? new InvalidOperationException("未在指定文件及其资源包中找到任何图标。");
        }

        /// <summary>
        /// 根据主程序路径动态枚举所有可能携带图标资源的候选文件（含同目录/卫星目录/.mui/.mun）。
        /// </summary>
        private static IReadOnlyList<string> BuildResourceProbeList(string binaryPath)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(string? candidate)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    return;
                }

                if (!File.Exists(candidate))
                {
                    return;
                }

                if (seen.Add(candidate))
                {
                    results.Add(candidate);
                }
            }

            TryAdd(binaryPath);

            var directory = Path.GetDirectoryName(binaryPath);
            var fileName = Path.GetFileName(binaryPath);
            if (string.IsNullOrEmpty(fileName))
            {
                return results;
            }

            // 收集需要探测的根目录，避免重复访问
            var probeRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(directory))
            {
                probeRoots.Add(directory);
            }

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windowsDir))
            {
                probeRoots.Add(windowsDir);
                probeRoots.Add(Path.Combine(windowsDir, "System32"));
                probeRoots.Add(Path.Combine(windowsDir, "SystemResources"));
            }

            var systemDir = Environment.SystemDirectory;
            if (!string.IsNullOrEmpty(systemDir))
            {
                probeRoots.Add(systemDir);
            }

            foreach (var root in probeRoots.Where(Directory.Exists))
            {
                AddSatelliteCandidates(root, fileName, TryAdd);
            }

            return results;
        }

        /// <summary>
        /// 在指定根目录下查找与给定文件名匹配的卫星资源文件（含同名、带文化后缀、文化子目录）。
        /// </summary>
        private static void AddSatelliteCandidates(string rootDirectory, string fileName, Action<string?> tryAdd)
        {
            if (string.IsNullOrEmpty(rootDirectory) || string.IsNullOrEmpty(fileName))
            {
                return;
            }

            tryAdd(Path.Combine(rootDirectory, fileName + ".mui"));
            tryAdd(Path.Combine(rootDirectory, fileName + ".mun"));

            // 形如 foo.exe.zh-CN.mui / foo.exe.2052.mui
            foreach (var file in SafeEnumerateFiles(rootDirectory, fileName + ".*.mui"))
            {
                tryAdd(file);
            }

            foreach (var file in SafeEnumerateFiles(rootDirectory, fileName + ".*.mun"))
            {
                tryAdd(file);
            }

            // 形如 zh-CN\foo.exe.mui
            foreach (var satelliteDir in SafeEnumerateDirectories(rootDirectory))
            {
                tryAdd(Path.Combine(satelliteDir, fileName + ".mui"));
                tryAdd(Path.Combine(satelliteDir, fileName + ".mun"));
            }
        }

        /// <summary>
        /// 安全遍历单层子目录，避免因权限或长路径导致异常中断。
        /// </summary>
        private static IEnumerable<string> SafeEnumerateDirectories(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                yield break;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(root);
            }
            catch
            {
                yield break;
            }

            foreach (var directory in directories)
            {
                yield return directory;
            }
        }

        /// <summary>
        /// 安全遍历指定目录下的文件，确保 I/O 异常可被吞掉。
        /// </summary>
        private static IEnumerable<string> SafeEnumerateFiles(string root, string searchPattern)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(searchPattern) || !Directory.Exists(root))
            {
                yield break;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                yield break;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
        private static IReadOnlyList<ExecutableIconCandidate> ExtractIconsFromFile(string path, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var peReader = new System.Reflection.PortableExecutable.PEReader(stream, System.Reflection.PortableExecutable.PEStreamOptions.LeaveOpen);
            var extractor = new ManagedExecutableIconExtractor(peReader);
            return extractor.ExtractIcons(cancellationToken);
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

        private sealed class ManagedExecutableIconExtractor
        {
            private const int RtIcon = 3;
            private const int RtGroupIcon = 14;

            private readonly PEReader _peReader;
            private readonly ImmutableArray<byte> _image;
            private readonly int _resourceBaseOffset;
            private readonly int _resourceLength;
            private readonly int _resourceDirectoryRva;
            private readonly List<ResourceRecord> _records = new();

            internal ManagedExecutableIconExtractor(PEReader peReader)
            {
                _peReader = peReader ?? throw new ArgumentNullException(nameof(peReader));
                _image = peReader.GetEntireImage().GetContent();

                var peHeader = peReader.PEHeaders?.PEHeader ?? throw new InvalidOperationException("无法读取 PE 头。");
                var directory = peHeader.ResourceTableDirectory;

                if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
                {
                    throw new InvalidOperationException("该文件不包含资源表。");
                }

                _resourceDirectoryRva = directory.RelativeVirtualAddress;
                _resourceBaseOffset = RvaToOffset(_resourceDirectoryRva);
                if (_resourceBaseOffset < 0)
                {
                    throw new InvalidOperationException("无法定位资源表所在的节。");
                }

                var maxReadableLength = _image.Length - _resourceBaseOffset;
                if (maxReadableLength <= 0)
                {
                    throw new InvalidOperationException("资源表范围无效。");
                }

                _resourceLength = maxReadableLength;
            }

            internal IReadOnlyList<ExecutableIconCandidate> ExtractIcons(System.Threading.CancellationToken cancellationToken)
            {
                if (_records.Count == 0)
                {
                    TraverseDirectory(0, level: 0, typeIdentifier: null, nameIdentifier: null, cancellationToken);
                }

                var iconLookup = BuildIconLookup();
                var groupRecords = _records.Where(r => r.TypeId == RtGroupIcon).ToList();

                var candidates = new List<ExecutableIconCandidate>(groupRecords.Count);
                foreach (var group in groupRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entries = IconResourceExtractor.ReadGroupEntries(group.Data);
                    if (entries.Count == 0)
                    {
                        continue;
                    }

                    var frames = entries
                        .Select(e => new IconFrameInfo(
                            IconResourceExtractor.NormalizeDimension(e.Width),
                            IconResourceExtractor.NormalizeDimension(e.Height),
                            e.BitCount))
                        .ToArray();

                    var iconPayloads = new List<byte[]>(entries.Count);
                    var missingResource = false;

                    foreach (var entry in entries)
                    {
                        if (!TryResolveIconBytes(entry.ResourceId, group.LanguageId, iconLookup, out var payload))
                        {
                            missingResource = true;
                            break;
                        }

                        iconPayloads.Add(payload);
                    }

                    if (missingResource)
                    {
                        continue;
                    }

                    var icoBytes = IconResourceExtractor.BuildIcoFromEntries(entries, iconPayloads);
                    var displayName = group.DisplayName;
                    var score = IconResourceExtractor.CalculateQualityScore(entries);
                    var groupId = group.TryGetNameId(out var nameId) ? nameId : -1;

                    candidates.Add(new ExecutableIconCandidate(displayName, groupId, group.LanguageId, frames, icoBytes, score));
                }

                return candidates
                    .OrderByDescending(c => c.QualityScore)
                    .ThenByDescending(c => c.Frames.Max(f => f.Width * f.Height))
                    .ToArray();
            }

            private void TraverseDirectory(int directoryOffset, int level, ResourceIdentifier? typeIdentifier, ResourceIdentifier? nameIdentifier, System.Threading.CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureRange(directoryOffset, 16);

                ushort namedEntries = ReadUInt16(directoryOffset + 12);
                ushort idEntries = ReadUInt16(directoryOffset + 14);
                int entryCount = namedEntries + idEntries;
                int entriesStart = directoryOffset + 16;

                for (int i = 0; i < entryCount; i++)
                {
                    var nameValue = ReadUInt32(entriesStart + i * 8);
                    var offsetValue = ReadUInt32(entriesStart + i * 8 + 4);
                    var identifier = ResolveIdentifier(nameValue);
                    bool isDirectory = (offsetValue & 0x8000_0000) != 0;
                    int targetOffset = (int)(offsetValue & 0x7FFF_FFFF);

                    if (isDirectory)
                    {
                        if (level == 0)
                        {
                            TraverseDirectory(targetOffset, 1, identifier, null, cancellationToken);
                        }
                        else if (level == 1)
                        {
                            TraverseDirectory(targetOffset, 2, typeIdentifier, identifier, cancellationToken);
                        }
                        else
                        {
                            TraverseDirectory(targetOffset, level + 1, typeIdentifier, nameIdentifier, cancellationToken);
                        }

                        continue;
                    }

                    if (typeIdentifier == null || nameIdentifier == null)
                    {
                        continue;
                    }

                    var dataEntry = ReadDataEntry(targetOffset);
                    AddResource(typeIdentifier.Value, nameIdentifier.Value, identifier, dataEntry);
                }
            }

            private Dictionary<int, Dictionary<int, byte[]>> BuildIconLookup()
            {
                var lookup = new Dictionary<int, Dictionary<int, byte[]>>();
                foreach (var record in _records.Where(r => r.TypeId == RtIcon))
                {
                    if (!record.TryGetNameId(out var iconId))
                    {
                        continue;
                    }

                    if (!lookup.TryGetValue(iconId, out var langMap))
                    {
                        langMap = new Dictionary<int, byte[]>();
                        lookup[iconId] = langMap;
                    }

                    langMap[record.LanguageId] = record.Data;
                }

                return lookup;
            }

            private bool TryResolveIconBytes(int resourceId, int languageId, Dictionary<int, Dictionary<int, byte[]>> lookup, out byte[] data)
            {
                if (lookup.TryGetValue(resourceId, out var langMap))
                {
                    if (langMap.TryGetValue(languageId, out data!))
                    {
                        return true;
                    }

                    if (langMap.TryGetValue(0, out data!))
                    {
                        return true;
                    }

                    if (langMap.Count > 0)
                    {
                        data = langMap.Values.First();
                        return true;
                    }
                }

                data = Array.Empty<byte>();
                return false;
            }

            private void AddResource(ResourceIdentifier typeIdentifier, ResourceIdentifier nameIdentifier, ResourceIdentifier languageIdentifier, ResourceDataEntry dataEntry)
            {
                if (!typeIdentifier.TryGetId(out int typeId))
                {
                    return;
                }

                int languageId = languageIdentifier.TryGetId(out var lang) ? lang : 0;
                var data = ReadResourcePayload(dataEntry);
                _records.Add(new ResourceRecord(typeId, nameIdentifier, languageId, data));
            }

            private byte[] ReadResourcePayload(ResourceDataEntry dataEntry)
            {
                if (dataEntry.Size <= 0)
                {
                    return Array.Empty<byte>();
                }

                var dataOffset = RvaToOffset(dataEntry.OffsetToData);
                if (dataOffset < 0 || dataOffset + dataEntry.Size > _image.Length)
                {
                    throw new InvalidOperationException("资源数据超出范围。");
                }

                return _image.AsSpan(dataOffset, dataEntry.Size).ToArray();
            }

            private ResourceDataEntry ReadDataEntry(int offset)
            {
                EnsureRange(offset, 16);
                var span = ReadSpan(offset, 16);

                return new ResourceDataEntry(
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8, 4)),
                    BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12, 4)));
            }

            private ResourceIdentifier ResolveIdentifier(uint value)
            {
                const uint nameMask = 0x8000_0000;
                if ((value & nameMask) == 0)
                {
                    return ResourceIdentifier.FromId((int)value);
                }

                int offset = (int)(value & ~nameMask);
                EnsureRange(offset, 2);
                ushort length = ReadUInt16(offset);
                int byteLength = length * 2;
                EnsureRange(offset + 2, byteLength);
                var span = ReadSpan(offset + 2, byteLength);
                var text = Encoding.Unicode.GetString(span);
                return ResourceIdentifier.FromName(text);
            }

            private ushort ReadUInt16(int offset)
            {
                EnsureRange(offset, 2);
                return BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(offset, 2));
            }

            private uint ReadUInt32(int offset)
            {
                EnsureRange(offset, 4);
                return BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(offset, 4));
            }

            private ReadOnlySpan<byte> ReadSpan(int relativeOffset, int length)
            {
                EnsureRange(relativeOffset, length);
                var start = _resourceBaseOffset + relativeOffset;
                return _image.AsSpan(start, length);
            }

            private void EnsureRange(int offset, int length)
            {
                if (offset < 0 || length < 0 || offset + length > _resourceLength)
                {
                    throw new InvalidOperationException("资源目录结构无效或已损坏。");
                }
            }

            private int RvaToOffset(int rva)
            {
                var relativeToResource = rva - _resourceDirectoryRva;
                if (relativeToResource >= 0 && relativeToResource < _resourceLength)
                {
                    return _resourceBaseOffset + relativeToResource;
                }

                foreach (var section in _peReader.PEHeaders!.SectionHeaders)
                {
                    var start = section.VirtualAddress;
                    var virtualSize = Math.Max(section.VirtualSize, section.SizeOfRawData);
                    if (virtualSize == 0)
                    {
                        continue;
                    }

                    if (rva < start || rva >= start + virtualSize)
                    {
                        continue;
                    }

                    if (section.SizeOfRawData == 0)
                    {
                        continue;
                    }

                    var relative = rva - start;
                    if (relative < 0 || relative >= section.SizeOfRawData)
                    {
                        continue;
                    }

                    var offset = section.PointerToRawData + relative;
                    if (offset >= 0 && offset < _image.Length)
                    {
                        return offset;
                    }
                    break;
                }

                return -1;
            }
        }

        private readonly struct ResourceRecord
        {
            internal ResourceRecord(int typeId, ResourceIdentifier nameIdentifier, int languageId, byte[] data)
            {
                TypeId = typeId;
                NameIdentifier = nameIdentifier;
                LanguageId = languageId;
                Data = data;
            }

            internal int TypeId { get; }
            internal ResourceIdentifier NameIdentifier { get; }
            internal int LanguageId { get; }
            internal byte[] Data { get; }

            internal bool TryGetNameId(out int value) => NameIdentifier.TryGetId(out value);
            internal string DisplayName => NameIdentifier.ToDisplayString();
        }

        private readonly struct ResourceDataEntry
        {
            internal ResourceDataEntry(int offsetToData, int size, int codePage, int reserved)
            {
                OffsetToData = offsetToData;
                Size = size;
                CodePage = codePage;
                Reserved = reserved;
            }

            internal int OffsetToData { get; }
            internal int Size { get; }
            internal int CodePage { get; }
            internal int Reserved { get; }
        }

        private readonly struct ResourceIdentifier
        {
            private ResourceIdentifier(int id)
            {
                Id = id;
                Name = null;
            }

            private ResourceIdentifier(string name)
            {
                Name = name;
                Id = null;
            }

            internal int? Id { get; }
            internal string? Name { get; }

            internal static ResourceIdentifier FromId(int id) => new(id);
            internal static ResourceIdentifier FromName(string name) => new(name);

            internal bool TryGetId(out int value)
            {
                if (Id.HasValue)
                {
                    value = Id.Value;
                    return true;
                }

                value = default;
                return false;
            }

            internal string ToDisplayString()
            {
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name!;
                }

                if (Id.HasValue)
                {
                    return Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                return "未命名";
            }
        }

        private static class IconResourceExtractor
        {
            internal static IReadOnlyList<GroupIconEntry> ReadGroupEntries(byte[] groupBytes)
            {
                if (groupBytes == null || groupBytes.Length < 6)
                {
                    return Array.Empty<GroupIconEntry>();
                }

                using var ms = new MemoryStream(groupBytes, writable: false);
                using var reader = new BinaryReader(ms);

                var reserved = reader.ReadUInt16();
                var type = reader.ReadUInt16();
                var count = reader.ReadUInt16();

                if (reserved != 0 || type != 1)
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

            internal static byte[] BuildIcoFromEntries(IReadOnlyList<GroupIconEntry> entries, IReadOnlyList<byte[]> iconData)
            {
                if (entries == null || entries.Count == 0)
                {
                    throw new InvalidOperationException("图标组为空。");
                }

                if (iconData == null || iconData.Count != entries.Count)
                {
                    throw new InvalidOperationException("图标数据与目录项数量不匹配。");
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

            internal static int NormalizeDimension(byte value) => value == 0 ? 256 : value;
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
    }
}
