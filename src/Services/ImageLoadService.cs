using SkiaSharp;
using Svg.Skia;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace IcoConverter.Services
{
    public class ImageLoadService : IImageLoadService
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp", ".svg", ".svgz"
        };

        private const int SignatureProbeLength = 32;
        private const int SvgSniffLength = 4096;

        public bool IsSupportedImageFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            return ResolveFormat(filePath) != ImageFileFormat.Unknown;
        }

        public ImageLoadResult LoadImage(string filePath, double targetDpi, int svgRenderMaxSize)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("图片路径为空。", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("找不到图片文件。", filePath);
            }

            var format = ResolveFormat(filePath);
            BitmapSource loaded = format switch
            {
                ImageFileFormat.Svg => LoadSvgBitmap(filePath, targetDpi, svgRenderMaxSize),
                ImageFileFormat.Raster => LoadRasterBitmap(filePath),
                _ => throw new NotSupportedException(
                    $"不支持的文件格式 '{Path.GetExtension(filePath)}'，请选择图片文件。")
            };

            var normalized = NormalizeDpi(loaded, targetDpi);
            return new ImageLoadResult(normalized, loaded.DpiX, loaded.DpiY);
        }

        // 优先检查文件头。部分 PNG 的 EXIF/XMP 中包含 "<svg" 文本，按扩展名或文本判断会误判
        private static ImageFileFormat ResolveFormat(string filePath)
        {
            var extension = Path.GetExtension(filePath);

            byte[] header;
            try
            {
                header = ReadHeaderBytes(filePath, SignatureProbeLength);
            }
            catch
            {
                return ImageFileFormat.Unknown;
            }

            if (HasRasterSignature(header))
            {
                return ImageFileFormat.Raster;
            }

            var isSvgExtension = string.Equals(extension, ".svg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".svgz", StringComparison.OrdinalIgnoreCase);

            if (IsGZip(header))
            {
                return isSvgExtension ? ImageFileFormat.Svg : ImageFileFormat.Unknown;
            }

            if (isSvgExtension)
            {
                return ImageFileFormat.Svg;
            }

            if (LooksLikeSvgText(filePath))
            {
                return ImageFileFormat.Svg;
            }

            return SupportedExtensions.Contains(extension) ? ImageFileFormat.Raster : ImageFileFormat.Unknown;
        }

        private static byte[] ReadHeaderBytes(string filePath, int length)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(length, (int)Math.Min(stream.Length, length))];
            var total = 0;
            while (total < buffer.Length)
            {
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0)
                {
                    break;
                }

                total += read;
            }

            return total == buffer.Length ? buffer : buffer[..total];
        }

        private static bool HasRasterSignature(ReadOnlySpan<byte> header)
        {
            // PNG
            if (header.Length >= 8
                && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            {
                return true;
            }

            // JPEG
            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return true;
            }

            // GIF
            if (MatchesAscii(header, "GIF8"))
            {
                return true;
            }

            // BMP
            if (MatchesAscii(header, "BM"))
            {
                return true;
            }

            // TIFF (II*\0 / MM\0*)
            if (header.Length >= 4
                && ((header[0] == 0x49 && header[1] == 0x49 && header[2] == 0x2A && header[3] == 0x00)
                    || (header[0] == 0x4D && header[1] == 0x4D && header[2] == 0x00 && header[3] == 0x2A)))
            {
                return true;
            }

            // ICO / CUR
            if (header.Length >= 4 && header[0] == 0x00 && header[1] == 0x00 && (header[2] == 0x01 || header[2] == 0x02) && header[3] == 0x00)
            {
                return true;
            }

            // WebP（RIFF....WEBP）
            if (header.Length >= 12 && MatchesAscii(header, "RIFF") && MatchesAscii(header, "WEBP", 8))
            {
                return true;
            }

            return false;
        }

        private static bool MatchesAscii(ReadOnlySpan<byte> buffer, string signature, int offset = 0)
        {
            if (buffer.Length < offset + signature.Length)
            {
                return false;
            }

            for (var i = 0; i < signature.Length; i++)
            {
                if (buffer[offset + i] != (byte)signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsGZip(ReadOnlySpan<byte> header)
            => header.Length >= 2 && header[0] == 0x1F && header[1] == 0x8B;

        // 回退探测仅检查文件开头的文本。完整 SVG 由后续解析器验证。
        private static bool LooksLikeSvgText(string filePath)
        {
            try
            {
                var bytes = ReadHeaderBytes(filePath, SvgSniffLength);
                if (bytes.Length == 0 || Array.IndexOf(bytes, (byte)0) >= 0)
                {
                    return false;
                }

                var text = DecodeText(bytes);
                return text.Contains("<svg", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // WPF 无法解码的格式（如 WebP）交由 SkiaSharp 处理，均失败时合并原因抛出
        private static BitmapSource LoadRasterBitmap(string filePath)
        {
            Exception? wpfError = null;
            try
            {
                return LoadRasterBitmapWithWpf(filePath);
            }
            catch (Exception ex)
            {
                wpfError = ex;
            }

            try
            {
                return LoadRasterBitmapWithSkia(filePath);
            }
            catch (Exception skiaError)
            {
                throw new InvalidOperationException(
                    $"无法解码图片 '{Path.GetFileName(filePath)}'：文件可能已损坏或使用了不受支持的编码。" +
                    $"（WPF: {DescribeError(wpfError)}；Skia: {DescribeError(skiaError)}）",
                    wpfError ?? skiaError);
            }
        }

        private static BitmapSource LoadRasterBitmapWithWpf(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // StreamSource 直接读取原始字节，路径中的 # 与 % 不会被当作 URI 转义
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            if (bitmap.CanFreeze)
            {
                bitmap.Freeze();
            }

            return bitmap;
        }

        private static BitmapSource LoadRasterBitmapWithSkia(string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var codec = SKCodec.Create(stream);
            if (codec == null)
            {
                throw new InvalidOperationException("SkiaSharp 无法识别该图片格式。");
            }

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("图片尺寸无效。");
            }

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                throw new InvalidOperationException($"SkiaSharp 解码失败: {result}。");
            }

            var source = BitmapSource.Create(
                width,
                height,
                96d,
                96d,
                PixelFormats.Pbgra32,
                null,
                bitmap.Bytes,
                bitmap.RowBytes);

            if (source.CanFreeze)
            {
                source.Freeze();
            }

            return source;
        }

        private static string DescribeError(Exception? exception)
            => exception == null ? "未知原因" : $"{exception.GetType().Name}: {exception.Message}";

        private static BitmapSource LoadSvgBitmap(string filePath, double targetDpi, int svgRenderMaxSize)
        {
            try
            {
                var svgContent = ReadSvgText(filePath);
                var sanitizedContent = SanitizeSvgForSkia(svgContent);

                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sanitizedContent));
                var svg = new SKSvg();
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
                    PixelFormats.Pbgra32,
                    null,
                    skBitmap.Bytes,
                    skBitmap.RowBytes);

                if (bitmapSource.CanFreeze)
                {
                    bitmapSource.Freeze();
                }

                return bitmapSource;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"无法解析 SVG 文件 '{Path.GetFileName(filePath)}'：{ex.Message}",
                    ex);
            }
        }

        // 检查 1F 8B 魔数判断是否为 gzip 压缩数据（SVGZ）
        private static string ReadSvgText(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);

            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                using var compressed = new MemoryStream(bytes);
                using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
                using var decompressed = new MemoryStream();
                gzip.CopyTo(decompressed);
                bytes = decompressed.ToArray();

                if (bytes.Length == 0)
                {
                    throw new InvalidOperationException("SVGZ 解压后内容为空。");
                }
            }

            return DecodeText(bytes);
        }

        // 识别 UTF-8 与 UTF-16 的 BOM，无 BOM 时按 UTF-8 解码。
        private static string DecodeText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }

            return Encoding.UTF8.GetString(bytes);
        }

        // 光栅化前清理已知的 SVG 渲染兼容问题。
        private static string SanitizeSvgForSkia(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return content;
            }

            // 清理前导 BOM 和空白，避免 XML 声明出现在文档起始位置之后。
            content = TrimLeadingMarkupNoise(content);

            try
            {
                var document = XDocument.Parse(content, LoadOptions.PreserveWhitespace);

                // 仅移除包含 feDropShadow 的滤镜，其余滤镜保留
                var unsupportedFilterIds = document
                    .Descendants()
                    .Where(e => e.Name.LocalName == "filter")
                    .Where(filter => filter.Descendants().Any(node => node.Name.LocalName == "feDropShadow"))
                    .Select(filter => filter.Attribute("id")?.Value)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (unsupportedFilterIds.Count > 0)
                {
                    foreach (var filter in document
                                 .Descendants()
                                 .Where(e => e.Name.LocalName == "filter")
                                 .ToArray())
                    {
                        var filterId = filter.Attribute("id")?.Value;
                        if (!string.IsNullOrWhiteSpace(filterId) && unsupportedFilterIds.Contains(filterId))
                        {
                            filter.Remove();
                        }
                    }

                    foreach (var element in document.Descendants())
                    {
                        var filterAttribute = element.Attribute("filter");
                        if (filterAttribute != null && TryGetFilterId(filterAttribute.Value, out var filterId) && unsupportedFilterIds.Contains(filterId))
                        {
                            filterAttribute.Remove();
                        }

                        var styleAttribute = element.Attribute("style");
                        if (styleAttribute != null)
                        {
                            var declarations = ParseStyleDeclarations(styleAttribute.Value);
                            if (declarations.TryGetValue("filter", out var styleFilterValue) &&
                                TryGetFilterId(styleFilterValue, out var styleFilterId) &&
                                unsupportedFilterIds.Contains(styleFilterId))
                            {
                                declarations.Remove("filter");
                                styleAttribute.Value = BuildStyleDeclarations(declarations);
                            }
                        }
                    }
                }

                foreach (var stop in document.Descendants().Where(e => e.Name.LocalName == "stop"))
                {
                    RewriteRgbaStopColor(stop);
                }

                return document.ToString(SaveOptions.DisableFormatting);
            }
            catch
            {
                // 清理为尽力而为，若结果异常则回退到原始文本
                return content;
            }
        }

        private static string TrimLeadingMarkupNoise(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return content;
            }

            var trimmed = content.TrimStart('\uFEFF');
            var index = trimmed.IndexOf('<');
            if (index > 0)
            {
                var prefix = trimmed.AsSpan(0, index);
                var onlyNoise = true;
                foreach (var c in prefix)
                {
                    if (c != '\uFEFF' && !char.IsWhiteSpace(c))
                    {
                        onlyNoise = false;
                        break;
                    }
                }

                if (onlyNoise)
                {
                    trimmed = trimmed[index..];
                }
            }

            return trimmed;
        }

        private enum ImageFileFormat
        {
            Unknown,
            Raster,
            Svg
        }

        // 将内联渐变样式的 rgba() 拆为 rgb() 和 stop-opacity，保留已有的 stop-opacity。
        private static void RewriteRgbaStopColor(XElement stopElement)
        {
            var styleAttribute = stopElement.Attribute("style");
            if (styleAttribute == null)
            {
                return;
            }

            var declarations = ParseStyleDeclarations(styleAttribute.Value);

            if (!declarations.TryGetValue("stop-color", out var stopColorValue))
            {
                return;
            }

            if (!TryParseRgba(stopColorValue, out var red, out var green, out var blue, out var alpha))
            {
                return;
            }

            declarations["stop-color"] = $"rgb({red},{green},{blue})";
            if (!declarations.ContainsKey("stop-opacity"))
            {
                declarations["stop-opacity"] = alpha.ToString("0.###", CultureInfo.InvariantCulture);
            }

            styleAttribute.Value = BuildStyleDeclarations(declarations);
        }

        private static bool TryParseRgba(string value, out int red, out int green, out int blue, out double alpha)
        {
            red = 0;
            green = 0;
            blue = 0;
            alpha = 1;

            const string Prefix = "rgba(";
            if (!value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !value.EndsWith(')'))
            {
                return false;
            }

            var inner = value.Substring(Prefix.Length, value.Length - Prefix.Length - 1);
            var parts = inner.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 4)
            {
                return false;
            }

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out red) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out green) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out blue) ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out alpha))
            {
                return false;
            }

            red = Math.Clamp(red, 0, 255);
            green = Math.Clamp(green, 0, 255);
            blue = Math.Clamp(blue, 0, 255);
            alpha = Math.Clamp(alpha, 0d, 1d);
            return true;
        }

        private static bool TryGetFilterId(string filterValue, out string filterId)
        {
            filterId = string.Empty;

            if (string.IsNullOrWhiteSpace(filterValue))
            {
                return false;
            }

            var trimmed = filterValue.Trim();
            const string Prefix = "url(#";
            if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(')'))
            {
                return false;
            }

            filterId = trimmed.Substring(Prefix.Length, trimmed.Length - Prefix.Length - 1);
            return !string.IsNullOrWhiteSpace(filterId);
        }

        private static Dictionary<string, string> ParseStyleDeclarations(string styleValue)
        {
            return styleValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Split(':', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildStyleDeclarations(Dictionary<string, string> declarations)
        {
            return string.Join(";", declarations.Select(pair => $"{pair.Key}:{pair.Value}"));
        }

        // 仅修改 DPI 元数据，像素不变。否则不同来源的图像在同一控件中显示尺寸不一致
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
