using IcoConverter.Models;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

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

            // 先构建可能包含图标资源的文件列表（主程序 + 各类卫星资源）
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
        /// <summary>
        /// 使用 Win32 API 从指定文件提取 RT_GROUP_ICON / RT_ICON 组合。
        /// </summary>
        private static IReadOnlyList<ExecutableIconCandidate> ExtractIconsFromFile(string path, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Win32IconResourceEnumerator.Extract(path, cancellationToken);
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
            var squareBitmap = originalBitmap.Width == originalBitmap.Height
                ? originalBitmap
                : FitBitmapToSquare(originalBitmap);

            try
            {
                // 为每个分辨率生成对应的图像数据（DIB 或 PNG），并记录尺寸和编码方式。
                var imagePayloads = new List<IconImagePayload>();

                foreach (var resolution in resolutions.OrderBy(r => r.Width))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    System.Drawing.Bitmap resized = null;
                    try
                    {
                        // 每个尺寸都从原图重新缩放，保证锐度，并立刻编码，减少中间格式转换。
                        resized = ResizeBitmap(squareBitmap, resolution.Width, resolution.Height);
                        var encodeAsPng = ShouldEncodeAsPng(resolution.Width, resolution.Height);
                        var data = encodeAsPng ? EncodeBitmapAsPng(resized!) : GetBitmapDibData(resized!);
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

                int currentOffset = 6 + (imagePayloads.Count * 16);

                // 写入目录条目（使用已编码的数据长度）。ICO 目录只记录尺寸/偏移，因此我们依赖前面得到的 bytes 数组。
                for (int i = 0; i < imagePayloads.Count; i++)
                {
                    var payload = imagePayloads[i];
                    var entry = CreateDirectoryEntry(payload.Width, payload.Height, payload.Data.Length, currentOffset, payload.IsPng);
                    ms.Write(entry, 0, entry.Length);
                    currentOffset += payload.Data.Length;
                }

                // 按顺序写入所有位图/PNG 数据，最终即可由 Icon 类直接读取。
                for (int i = 0; i < imagePayloads.Count; i++)
                {
                    var payload = imagePayloads[i];
                    ms.Write(payload.Data, 0, payload.Data.Length);
                }

                // 返回图标对象（从内存复制字节以避免对原流的依赖）
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
        private static byte[] CreateDirectoryEntry(int widthPx, int heightPx, int dataSize, int offset, bool isPng)
        {
            using var ms = new MemoryStream();
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

            // 直接读取 BGRA 像素到托管数组，便于后续按 ICO 规则倒序写出。
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

            // XOR 位图数据（按自底向上顺序写入，遵循 DIB 布局）。
            for (int y = height - 1; y >= 0; y--)
            {
                bw.Write(pixels, y * stride, stride);
            }

            // AND mask（32bpp 带 alpha 时可全 0，用于兼容旧版渲染管线）。
            var mask = new byte[maskStride * height];
            bw.Write(mask);

            return ms.ToArray();
        }

        /// <summary>
        /// 确保位图为 32bpp ARGB，避免格式不一致导致写入错误。
        /// </summary>
        private static System.Drawing.Bitmap Ensure32bppArgb(System.Drawing.Bitmap source)
        {
            if (source.PixelFormat == DrawingPixelFormat.Format32bppArgb)
            {
                return (System.Drawing.Bitmap)source.Clone();
            }

            var converted = new System.Drawing.Bitmap(source.Width, source.Height, DrawingPixelFormat.Format32bppArgb);
            using var g = System.Drawing.Graphics.FromImage(converted);
            // SourceCopy 可避免 alpha 被再次混合，确保透明度信息完整。
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            return converted;
        }

        /// <summary>
        /// 将 WPF 的 <see cref="BitmapSource"/> 的像素复制到 <see cref="System.Drawing.Bitmap"/> 中。
        /// </summary>
        private static System.Drawing.Bitmap BitmapSourceToBitmap(BitmapSource source)
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

            // WPF 的 BitmapSource 与 GDI+ 位图内存布局不同，需要手动 CopyPixels。
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
            var resized = new System.Drawing.Bitmap(width, height, DrawingPixelFormat.Format32bppArgb);

            using (var graphics = System.Drawing.Graphics.FromImage(resized))
            {
                // 使用高质量插值/抗锯齿，保证图标在小尺寸下仍保持清晰。
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;

                graphics.DrawImage(original, 0, 0, width, height);
            }

            return resized;
        }

        private static byte[] EncodeBitmapAsPng(System.Drawing.Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        private static bool ShouldEncodeAsPng(int width, int height)
        {
            return Math.Max(width, height) >= 512;
        }

        private static System.Drawing.Bitmap FitBitmapToSquare(System.Drawing.Bitmap source)
        {
            var size = Math.Max(source.Width, source.Height);
            var square = new System.Drawing.Bitmap(size, size, DrawingPixelFormat.Format32bppArgb);

            using var graphics = System.Drawing.Graphics.FromImage(square);
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            var scale = (float)size / Math.Min(source.Width, source.Height);
            var scaledWidth = source.Width * scale;
            var scaledHeight = source.Height * scale;
            var offsetX = (size - scaledWidth) / 2f;
            var offsetY = (size - scaledHeight) / 2f;
            graphics.DrawImage(source, offsetX, offsetY, scaledWidth, scaledHeight);

            return square;
        }

        private sealed record IconImagePayload(int Width, int Height, byte[] Data, bool IsPng);

        /// <summary>
        /// 负责使用 Win32 资源 API 枚举并拼装 ICON 结果的辅助类。
        /// </summary>
        private sealed class Win32IconResourceEnumerator
        {
            private const int RtIcon = 3;
            private const int RtGroupIcon = 14;
            private const int ErrorResourceTypeNotFound = 1813;
            private const int ErrorResourceLangNotFound = 1815;

            private readonly SafeLibraryHandle _libraryHandle;
            private readonly IntPtr _moduleHandle;
            private readonly System.Threading.CancellationToken _cancellationToken;
            private readonly List<GroupResourceRecord> _groupRecords = new();
            private readonly Dictionary<(ushort Lang, ushort Id), byte[]> _iconCache = new();

            private Win32IconResourceEnumerator(SafeLibraryHandle libraryHandle, System.Threading.CancellationToken cancellationToken)
            {
                _libraryHandle = libraryHandle;
                _moduleHandle = libraryHandle.DangerousGetHandle();
                _cancellationToken = cancellationToken;
            }

            internal static IReadOnlyList<ExecutableIconCandidate> Extract(string path, System.Threading.CancellationToken cancellationToken)
            {
                var flags = NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE | NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_IMAGE_RESOURCE;
                var handle = NativeMethods.LoadLibraryEx(path, IntPtr.Zero, flags);
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法加载文件 '{path}' 以扫描图标资源。");
                }

                using (handle)
                {
                    var enumerator = new Win32IconResourceEnumerator(handle, cancellationToken);
                    enumerator.EnumerateGroupResources();
                    return enumerator.BuildCandidates();
                }
            }

            private void EnumerateGroupResources()
            {
                var gcHandle = GCHandle.Alloc(this);
                try
                {
                    if (!NativeMethods.EnumResourceNames(_moduleHandle, new IntPtr(RtGroupIcon), EnumResourceNameThunk, GCHandle.ToIntPtr(gcHandle)))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorResourceTypeNotFound)
                        {
                            throw new Win32Exception(error, "枚举图标组资源失败。");
                        }
                    }
                }
                finally
                {
                    gcHandle.Free();
                }
            }

            private static bool EnumResourceNameThunk(IntPtr hModule, IntPtr type, IntPtr name, IntPtr parameter)
            {
                var handle = GCHandle.FromIntPtr(parameter);
                if (handle.Target is Win32IconResourceEnumerator enumerator)
                {
                    return enumerator.HandleGroupResource(type, name);
                }

                return false;
            }

            private bool HandleGroupResource(IntPtr type, IntPtr name)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var identifier = ResourceIdentifier.FromWin32Value(name);
                var context = new GroupLanguageContext(this, identifier);
                var gcHandle = GCHandle.Alloc(context);

                try
                {
                    if (!NativeMethods.EnumResourceLanguages(_moduleHandle, type, name, EnumResourceLanguageThunk, GCHandle.ToIntPtr(gcHandle)))
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorResourceLangNotFound)
                        {
                            throw new Win32Exception(error, $"枚举资源语言失败: {identifier.ToDisplayString()}");
                        }
                    }
                }
                finally
                {
                    gcHandle.Free();
                }

                return true;
            }

            private static bool EnumResourceLanguageThunk(IntPtr hModule, IntPtr type, IntPtr name, ushort language, IntPtr parameter)
            {
                var handle = GCHandle.FromIntPtr(parameter);
                if (handle.Target is GroupLanguageContext context)
                {
                    return context.Owner.HandleGroupLanguage(context.Identifier, type, name, language);
                }

                return false;
            }

            private bool HandleGroupLanguage(ResourceIdentifier identifier, IntPtr type, IntPtr name, ushort language)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var data = ReadResourceBytes(type, name, language);
                if (data.Length == 0)
                {
                    return true;
                }

                _groupRecords.Add(new GroupResourceRecord(identifier, language, data));
                return true;
            }

            private byte[] ReadResourceBytes(IntPtr type, IntPtr name, ushort language)
            {
                var resourceHandle = NativeMethods.FindResourceEx(_moduleHandle, type, name, language);
                if (resourceHandle == IntPtr.Zero)
                {
                    return Array.Empty<byte>();
                }

                var size = NativeMethods.SizeofResource(_moduleHandle, resourceHandle);
                if (size == 0)
                {
                    return Array.Empty<byte>();
                }

                if (size > int.MaxValue)
                {
                    throw new InvalidOperationException("资源数据过大，无法加载。");
                }

                var dataHandle = NativeMethods.LoadResource(_moduleHandle, resourceHandle);
                if (dataHandle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "加载资源失败。");
                }

                var pointer = NativeMethods.LockResource(dataHandle);
                if (pointer == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "锁定资源失败。");
                }

                var buffer = new byte[size];
                Marshal.Copy(pointer, buffer, 0, (int)size);
                return buffer;
            }

            /// <summary>
            /// 把枚举到的图标组与对应位图数据转换为 UI 可用的候选项。
            /// </summary>
            private IReadOnlyList<ExecutableIconCandidate> BuildCandidates()
            {
                if (_groupRecords.Count == 0)
                {
                    return Array.Empty<ExecutableIconCandidate>();
                }

                var candidates = new List<ExecutableIconCandidate>(_groupRecords.Count);

                foreach (var group in _groupRecords)
                {
                    _cancellationToken.ThrowIfCancellationRequested();

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
                    var missing = false;

                    foreach (var entry in entries)
                    {
                        if (!TryLoadIconEntry(entry.ResourceId, group.LanguageId, out var payload))
                        {
                            missing = true;
                            break;
                        }

                        iconPayloads.Add(payload);
                    }

                    if (missing)
                    {
                        continue;
                    }

                    var icoBytes = IconResourceExtractor.BuildIcoFromEntries(entries, iconPayloads);
                    var score = IconResourceExtractor.CalculateQualityScore(entries);
                    var groupId = group.NameIdentifier.TryGetId(out var id) ? id : -1;

                    candidates.Add(new ExecutableIconCandidate(
                        group.NameIdentifier.ToDisplayString(),
                        groupId,
                        group.LanguageId,
                        frames,
                        icoBytes,
                        score));
                }

                return candidates
                    .OrderByDescending(c => c.QualityScore)
                    .ThenByDescending(c => c.Frames.Max(f => f.Width * f.Height))
                    .ToArray();
            }

            private bool TryLoadIconEntry(ushort iconId, ushort preferredLanguage, out byte[] data)
            {
                if (TryGetCached(preferredLanguage, iconId, out data))
                {
                    return true;
                }

                if (TryReadIconBytes(iconId, preferredLanguage, out data))
                {
                    _iconCache[(preferredLanguage, iconId)] = data;
                    return true;
                }

                if (preferredLanguage != 0 && TryReadIconBytes(iconId, 0, out data))
                {
                    _iconCache[(0, iconId)] = data;
                    return true;
                }

                foreach (var language in EnumerateIconLanguages(iconId))
                {
                    if (language == preferredLanguage)
                    {
                        continue;
                    }

                    if (TryReadIconBytes(iconId, language, out data))
                    {
                        _iconCache[(language, iconId)] = data;
                        return true;
                    }
                }

                data = Array.Empty<byte>();
                return false;
            }

            private bool TryGetCached(ushort language, ushort iconId, out byte[] data)
            {
                return _iconCache.TryGetValue((language, iconId), out data!);
            }

            private bool TryReadIconBytes(ushort iconId, ushort language, out byte[] data)
            {
                var bytes = ReadResourceBytes(new IntPtr(RtIcon), new IntPtr(iconId), language);
                if (bytes.Length == 0)
                {
                    data = Array.Empty<byte>();
                    return false;
                }

                data = bytes;
                return true;
            }

            /// <summary>
            /// 列举单个 RT_ICON 资源所有可用语言，便于做语言回退。
            /// </summary>
            private IEnumerable<ushort> EnumerateIconLanguages(ushort iconId)
            {
                var languages = new List<ushort>();
                var gcHandle = GCHandle.Alloc(languages);
                try
                {
                    NativeMethods.EnumResourceLanguages(_moduleHandle, new IntPtr(RtIcon), new IntPtr(iconId), IconLanguageEnumThunk, GCHandle.ToIntPtr(gcHandle));
                }
                finally
                {
                    gcHandle.Free();
                }

                return languages;
            }

            private static bool IconLanguageEnumThunk(IntPtr hModule, IntPtr type, IntPtr name, ushort language, IntPtr parameter)
            {
                var handle = GCHandle.FromIntPtr(parameter);
                if (handle.Target is List<ushort> languages && !languages.Contains(language))
                {
                    languages.Add(language);
                }

                return true;
            }

            private sealed class GroupLanguageContext
            {
                internal GroupLanguageContext(Win32IconResourceEnumerator owner, ResourceIdentifier identifier)
                {
                    Owner = owner;
                    Identifier = identifier;
                }

                internal Win32IconResourceEnumerator Owner { get; }
                internal ResourceIdentifier Identifier { get; }
            }
        }

        private readonly struct GroupResourceRecord
        {
            internal GroupResourceRecord(ResourceIdentifier nameIdentifier, ushort languageId, byte[] data)
            {
                NameIdentifier = nameIdentifier;
                LanguageId = languageId;
                Data = data;
            }

            internal ResourceIdentifier NameIdentifier { get; }
            internal ushort LanguageId { get; }
            internal byte[] Data { get; }
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
            internal static ResourceIdentifier FromWin32Value(IntPtr value)
            {
                if (IsIntResource(value))
                {
                    return FromId(value.ToInt32());
                }

                var text = Marshal.PtrToStringUni(value) ?? string.Empty;
                return FromName(text);
            }

            private static bool IsIntResource(IntPtr value) => ((ulong)value.ToInt64() >> 16) == 0;

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

        /// <summary>
        /// Win32 P/Invoke 声明；仅包含图标枚举所需的内核 API。
        /// </summary>
        private static class NativeMethods
        {
            [Flags]
            internal enum LoadLibraryFlags : uint
            {
                DONT_RESOLVE_DLL_REFERENCES = 0x0000_0001,
                LOAD_LIBRARY_AS_DATAFILE = 0x0000_0002,
                LOAD_WITH_ALTERED_SEARCH_PATH = 0x0000_0008,
                LOAD_IGNORE_CODE_AUTHZ_LEVEL = 0x0000_0010,
                LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x0000_0020,
                LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE = 0x0000_0040,
            }

            internal delegate bool EnumResourceNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);
            internal delegate bool EnumResourceLangProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, ushort wIDLanguage, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern SafeLibraryHandle LoadLibraryEx(string lpFileName, IntPtr hFile, LoadLibraryFlags dwFlags);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResourceNameProc lpEnumFunc, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern bool EnumResourceLanguages(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, EnumResourceLangProc lpEnumFunc, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern IntPtr FindResourceEx(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLanguage);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr LockResource(IntPtr hResData);

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);
        }

        private sealed class SafeLibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeLibraryHandle()
                : base(true)
            {
            }

            protected override bool ReleaseHandle() => NativeMethods.FreeLibrary(handle);
        }
    }
}
