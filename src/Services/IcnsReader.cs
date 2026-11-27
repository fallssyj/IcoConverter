using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace IcoConverter.Services
{
    /// <summary>
    /// ICNS 中的 PNG 帧。优先读取 PNG 头中的尺寸，无法读取时使用类型标识对应的尺寸。
    /// </summary>
    internal sealed class IcnsFrame
    {
        public IcnsFrame(string type, int width, int height, byte[] pngData)
        {
            Type = type;
            Width = width;
            Height = height;
            PngData = pngData;
        }

        /// <summary>元素类型标识，比如 ic08 / ic13。</summary>
        public string Type { get; }

        public int Width { get; }

        public int Height { get; }

        public byte[] PngData { get; }
    }

    /// <summary>
    /// 读取 ICNS 中受支持类型的 PNG 载荷。
    /// 跳过旧式位图类型和 JPEG2000 等非 PNG 载荷。
    /// </summary>
    internal static class IcnsReader
    {
        private const int ElementHeaderSize = 8;
        private const int PngHeaderSize = 24;

        // 支持的类型及其标称像素尺寸。普通类型和 @2x 类型可以对应相同尺寸。
        private static readonly Dictionary<string, int> PngTypeSizes = new(StringComparer.Ordinal)
        {
            ["icp4"] = 16,
            ["icp5"] = 32,
            ["icp6"] = 64,
            ["ic07"] = 128,
            ["ic08"] = 256,
            ["ic09"] = 512,
            ["ic10"] = 1024,
            ["ic11"] = 32,   // 16pt @2x
            ["ic12"] = 64,   // 32pt @2x
            ["ic13"] = 256,  // 128pt @2x
            ["ic14"] = 512,  // 256pt @2x
        };

        // 仅用于统计，以便在全部解析失败时给出具体原因
        private static readonly HashSet<string> LegacyRasterTypes = new(StringComparer.Ordinal)
        {
            "ic04", "ic05", "ic06",
            "is32", "il32", "ih32", "it32",
            "s8mk", "l8mk", "h8mk", "t8mk",
        };

        public static bool HasIcnsSignature(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var head = new byte[4];
                return stream.Read(head, 0, head.Length) == head.Length && IsMagic(head);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static IReadOnlyList<IcnsFrame> ReadFrames(string path)
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < ElementHeaderSize || !IsMagic(data.AsSpan(0, 4)))
            {
                throw new InvalidOperationException("文件格式无效，未识别为 ICNS。");
            }

            // 声明长度有效时按声明长度解析，忽略尾部数据。长度字段无效时按实际文件长度解析。
            var declaredLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
            var limit = declaredLength >= ElementHeaderSize && declaredLength <= data.Length
                ? (long)declaredLength
                : data.Length;

            var frames = new List<IcnsFrame>();
            var legacyCount = 0;
            var broken = false;
            long offset = ElementHeaderSize;

            while (offset + ElementHeaderSize <= limit)
            {
                var type = Encoding.ASCII.GetString(data, (int)offset, 4);
                var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset + 4, 4));

                // 长度字段异常时无法定位下一项，终止解析
                if (length < ElementHeaderSize || offset + length > limit)
                {
                    broken = true;
                    break;
                }

                var payload = data.AsSpan((int)offset + ElementHeaderSize, (int)length - ElementHeaderSize);
                if (PngTypeSizes.TryGetValue(type, out var slotSize))
                {
                    if (TryReadPngSize(payload, out var width, out var height))
                    {
                        frames.Add(new IcnsFrame(type, width, height, payload.ToArray()));
                    }
                    else if (IsPng(payload))
                    {
                        // 载荷有 PNG 签名但无法读取宽高时，按类型登记尺寸，解码由调用方处理。
                        frames.Add(new IcnsFrame(type, slotSize, slotSize, payload.ToArray()));
                    }
                }
                else if (LegacyRasterTypes.Contains(type))
                {
                    legacyCount++;
                }

                offset += length;
            }

            if (frames.Count == 0)
            {
                if (legacyCount > 0)
                {
                    throw new InvalidOperationException(
                        "这个 ICNS 里只有旧式的 ARGB / JPEG2000 图像，暂不支持读取。可用 macOS 的 iconutil 转一份再试。");
                }

                throw new InvalidOperationException(broken
                    ? "ICNS 结构不完整，文件可能已损坏或被截断。"
                    : "ICNS 中没有可用图像。");
            }

            return frames;
        }

        private static bool IsMagic(ReadOnlySpan<byte> span)
            => span.Length >= 4
               && span[0] == (byte)'i'
               && span[1] == (byte)'c'
               && span[2] == (byte)'n'
               && span[3] == (byte)'s';

        private static bool IsPng(ReadOnlySpan<byte> span)
            => span.Length >= 8
               && span[0] == 0x89 && span[1] == 0x50 && span[2] == 0x4E && span[3] == 0x47
               && span[4] == 0x0D && span[5] == 0x0A && span[6] == 0x1A && span[7] == 0x0A;

        // IHDR 紧跟签名和长度标记，宽高固定在第 16、20 字节
        private static bool TryReadPngSize(ReadOnlySpan<byte> payload, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (payload.Length < PngHeaderSize || !IsPng(payload))
            {
                return false;
            }

            var w = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(16, 4));
            var h = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(20, 4));
            if (w <= 0 || h <= 0)
            {
                return false;
            }

            width = w;
            height = h;
            return true;
        }
    }
}
