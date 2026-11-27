using IcoConverter.Models;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Size = System.Drawing.Size;

namespace IcoConverter.Services
{
    /// <summary>
    /// 命令行模式。指定 --out 时不创建窗口，处理完成后直接退出，供脚本与 CI 调用。
    /// 此处不复用 MainViewModel：该层依赖界面状态，在无 UI 场景下不适用。
    /// </summary>
    internal static class CliRunner
    {
        private const int SvgRenderMaxSize = 1024;

        private static readonly Size[] DefaultIcoSizes =
        {
            new(16, 16), new(24, 24), new(32, 32), new(48, 48), new(64, 64), new(72, 72), new(96, 96), new(128, 128), new(256, 256)
        };

        private static readonly int[] DefaultPngSetSizes = { 16, 32, 48, 64, 128, 256, 512 };

        public static bool IsCliInvocation(string[] args) =>
            args.Any(argument => string.Equals(argument, "--out", StringComparison.OrdinalIgnoreCase));

        public static int Run(string[] args)
        {
            // WinExe 不附带控制台，需挂接到父进程的控制台才能输出
            AttachConsole(-1);
            var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };

            try
            {
                if (args.Any(argument => string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase)))
                {
                    WriteUsage(output);
                    return 0;
                }

                if (!TryParse(args, out var options, out var error))
                {
                    output.WriteLine($"参数错误: {error}");
                    WriteUsage(output);
                    return 2;
                }

                return Export(options, output);
            }
            catch (Exception ex)
            {
                output.WriteLine($"失败: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static int Export(CliOptions options, TextWriter output)
        {
            if (!File.Exists(options.InputPath))
            {
                output.WriteLine($"失败: 找不到文件 {options.InputPath}");
                return 1;
            }

            var loadService = new ImageLoadService();
            if (!loadService.IsSupportedImageFile(options.InputPath))
            {
                output.WriteLine($"失败: 不支持的图片格式 {Path.GetExtension(options.InputPath)}");
                return 1;
            }

            output.WriteLine($"源图: {options.InputPath}");
            var loadResult = loadService.LoadImage(options.InputPath, 96d, SvgRenderMaxSize);
            BitmapSource image = loadResult.Image;
            output.WriteLine($"尺寸: {image.PixelWidth}×{image.PixelHeight}");

            var processor = new ImageProcessor();

            if (options.TrimPaddingPercent > 0)
            {
                var trimmed = processor.TrimToContent(image, options.TrimPaddingPercent);
                if (!ReferenceEquals(trimmed, image))
                {
                    output.WriteLine($"对齐主体: {image.PixelWidth}×{image.PixelHeight} → {trimmed.PixelWidth}×{trimmed.PixelHeight}");
                }

                image = trimmed;
            }

            // 圆角为 0 且无需扩展画布时跳过蒙版处理，避免一次全图拷贝
            if (options.CornerRadiusPixels > 0 || options.ExpandCanvasPixels != 0)
            {
                var renderOptions = new ImageRenderOptions(
                    MaskShape.RoundedRectangle,
                    options.CornerRadiusPixels,
                    ImageTransformOptions.Identity,
                    CircleFitMode.Inscribed,
                    0,
                    true,
                    options.ExpandCanvasPixels,
                    Colors.Transparent,
                    false);
                image = processor.ApplyMask(image, renderOptions);
                output.WriteLine($"已应用蒙版: 圆角 {options.CornerRadiusPixels}px，画布扩展 {options.ExpandCanvasPixels}px");
            }

            if (image is Freezable freezable && !freezable.IsFrozen)
            {
                freezable.Freeze();
            }

            return options.Format switch
            {
                CliFormat.Icns => WriteIcns(image, options, output),
                CliFormat.PngSet => WritePngSet(image, options, output),
                _ => WriteIco(image, options, output)
            };
        }

        private static int WriteIco(BitmapSource image, CliOptions options, TextWriter output)
        {
            var sizes = options.Sizes.Count > 0 ? options.Sizes : DefaultIcoSizes.ToList();
            var service = new IcoConverterService();
            service.ConvertToIcoAsync(image, options.OutputPath, sizes, default, options.FrameMode).GetAwaiter().GetResult();

            output.WriteLine($"已写入 ICO: {options.OutputPath}（{sizes.Count} 个尺寸）");
            return 0;
        }

        private static int WriteIcns(BitmapSource image, CliOptions options, TextWriter output)
        {
            // 未指定时使用 ICNS 自身的完整档位，不沿用 ICO 的尺寸集合
            var sizes = options.Sizes.Count > 0
                ? options.Sizes
                : IcoResolution.IcnsSizes.Select(size => new Size(size, size)).ToList();
            var service = new IcnsConverterService();
            var result = service.ConvertToIcnsAsync(image, options.OutputPath, sizes).GetAwaiter().GetResult();

            output.WriteLine($"已写入 ICNS: {options.OutputPath}（{result.WrittenSizes.Count} 档，含 @2x 别名共 {result.EntryCount} 个元素）");

            if (result.SkippedSizes.Count > 0)
            {
                var skipped = string.Join("、", result.SkippedSizes.Select(size => $"{size.Width}×{size.Height}"));
                output.WriteLine($"跳过: {skipped}（ICNS 没有这些档位）");
            }

            return 0;
        }

        private static int WritePngSet(BitmapSource image, CliOptions options, TextWriter output)
        {
            var sizes = options.Sizes.Count > 0
                ? options.Sizes
                : DefaultPngSetSizes.Select(size => new Size(size, size)).ToList();

            // PNG 集将带扩展名的路径拆成输出目录和命名前缀。
            var looksLikeFile = Path.HasExtension(options.OutputPath);
            var folder = looksLikeFile
                ? Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!
                : options.OutputPath;

            Directory.CreateDirectory(folder);

            var baseName = Path.GetFileNameWithoutExtension(looksLikeFile ? options.OutputPath : options.InputPath);
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "icon";
            }

            var outputs = sizes.Select(size =>
            {
                var suffix = size.Width == size.Height ? $"{size.Width}" : $"{size.Width}x{size.Height}";
                return (size, $"{baseName}-{suffix}.png");
            });
            var written = PngIconSetWriter.Write(image, folder, outputs);

            output.WriteLine($"已写入 {written} 张 PNG: {folder}");
            return 0;
        }

        private static bool TryParse(string[] args, out CliOptions options, out string error)
        {
            options = new CliOptions();
            error = string.Empty;

            for (var i = 0; i < args.Length; i++)
            {
                var argument = args[i];
                switch (argument.ToLowerInvariant())
                {
                    case "--out":
                        if (!TryTakeValue(args, ref i, out var outPath))
                        {
                            error = "--out 后面要跟输出路径";
                            return false;
                        }

                        options.OutputPath = outPath!;
                        break;

                    case "--sizes":
                        if (!TryTakeValue(args, ref i, out var sizeText) || !TryParseSizes(sizeText!, out var sizes))
                        {
                            error = "--sizes 格式应为 16,32,48,256";
                            return false;
                        }

                        options.Sizes = sizes;
                        break;

                    case "--corner":
                        if (!TryTakeValue(args, ref i, out var cornerText) || !int.TryParse(cornerText, out var corner))
                        {
                            error = "--corner 需要整数像素值";
                            return false;
                        }

                        options.CornerRadiusPixels = Math.Max(0, corner);
                        break;

                    case "--expand":
                        if (!TryTakeValue(args, ref i, out var expandText) || !int.TryParse(expandText, out var expand))
                        {
                            error = "--expand 需要整数像素值";
                            return false;
                        }

                        options.ExpandCanvasPixels = expand;
                        break;

                    case "--padding":
                        if (!TryTakeValue(args, ref i, out var paddingText) || !int.TryParse(paddingText, out var padding))
                        {
                            error = "--padding 需要 0-45 的整数";
                            return false;
                        }

                        options.TrimPaddingPercent = Math.Clamp(padding, 0, 45);
                        break;

                    case "--frame":
                        if (!TryTakeValue(args, ref i, out var frameText) ||
                            frameText!.ToLowerInvariant() is not ("auto" or "png" or "bmp"))
                        {
                            error = "--frame 必须是 auto、png 或 bmp";
                            return false;
                        }

                        options.FrameMode = frameText.ToLowerInvariant() switch
                        {
                            "png" => IcoPngFrameMode.AlwaysPng,
                            "bmp" => IcoPngFrameMode.NeverPng,
                            _ => IcoPngFrameMode.Auto
                        };
                        break;

                    case "--icns":
                        options.Format = CliFormat.Icns;
                        break;

                    case "--pngset":
                        options.Format = CliFormat.PngSet;
                        break;

                    case "--ico":
                        options.Format = CliFormat.Ico;
                        break;

                    default:
                        if (argument.StartsWith("--", StringComparison.Ordinal))
                        {
                            error = $"不认识的参数 {argument}";
                            return false;
                        }

                        if (string.IsNullOrEmpty(options.InputPath))
                        {
                            options.InputPath = argument;
                        }

                        break;
                }
            }

            if (string.IsNullOrEmpty(options.InputPath))
            {
                error = "缺少输入文件";
                return false;
            }

            if (string.IsNullOrEmpty(options.OutputPath))
            {
                error = "缺少 --out";
                return false;
            }

            // 未指定 --ico / --icns / --pngset 时按输出扩展名推断格式
            if (options.Format == CliFormat.Unspecified)
            {
                var extension = Path.GetExtension(options.OutputPath).ToLowerInvariant();
                options.Format = extension switch
                {
                    ".icns" => CliFormat.Icns,
                    ".ico" => CliFormat.Ico,
                    _ => CliFormat.PngSet
                };
            }

            return true;
        }

        private static bool TryTakeValue(string[] args, ref int index, out string? value)
        {
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = null;
                return false;
            }

            value = args[++index];
            return true;
        }

        private static bool TryParseSizes(string text, out List<Size> sizes)
        {
            sizes = new List<Size>();

            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // 尺寸支持单个边长或宽×高。输入宽高时以 x 或 X 分隔。
                var pieces = part.Split('x', 'X');
                if (!int.TryParse(pieces[0], out var width) || width <= 0)
                {
                    return false;
                }

                var height = width;
                if (pieces.Length > 1 && (!int.TryParse(pieces[1], out height) || height <= 0))
                {
                    return false;
                }

                var size = new Size(width, height);
                if (!sizes.Contains(size))
                {
                    sizes.Add(size);
                }
            }

            return sizes.Count > 0;
        }

        private static void WriteUsage(TextWriter output)
        {
            output.WriteLine("IcoConverter 命令行");
            output.WriteLine("  IcoConverter.exe <输入图片> --out <输出路径> [参数]");
            output.WriteLine();
            output.WriteLine("参数:");
            output.WriteLine("  --ico | --icns | --pngset   输出格式，不写就按 --out 的扩展名猜");
            output.WriteLine("  --sizes 16,32,48,256        尺寸列表，PNG 集默认 16/32/48/64/128/256/512");
            output.WriteLine("  --corner 20                 圆角半径（像素）");
            output.WriteLine("  --expand 0                  画布扩展，负数向内收");
            output.WriteLine("  --padding 8                 自动裁掉四周空白，再按百分比补边；0 表示不对齐");
            output.WriteLine("  --frame auto                 ICO 帧编码：auto / png / bmp");
            output.WriteLine();
            output.WriteLine("示例:");
            output.WriteLine("  IcoConverter.exe logo.png --out app.ico --sizes 16,32,48,256 --corner 40");
            output.WriteLine("  IcoConverter.exe logo.png --out icons --pngset --padding 10");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        private enum CliFormat
        {
            Unspecified,
            Ico,
            Icns,
            PngSet
        }

        private sealed class CliOptions
        {
            public string InputPath { get; set; } = string.Empty;

            public string OutputPath { get; set; } = string.Empty;

            public CliFormat Format { get; set; } = CliFormat.Unspecified;

            public List<Size> Sizes { get; set; } = new();

            public int CornerRadiusPixels { get; set; }

            public int ExpandCanvasPixels { get; set; }

            public int TrimPaddingPercent { get; set; }

            public IcoPngFrameMode FrameMode { get; set; } = IcoPngFrameMode.Auto;
        }
    }
}
