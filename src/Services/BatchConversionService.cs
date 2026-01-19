using IcoConverter.ViewModels;
using System.Drawing;
using System.IO;

namespace IcoConverter.Services
{
    /// <summary>
    /// 批量转换服务实现：单文件失败不影响整体。
    /// </summary>
    public class BatchConversionService : IBatchConversionService
    {
        private readonly IImageLoadService _imageLoadService;
        private readonly IImageProcessor _imageProcessor;
        private readonly IIcoConverterService _icoConverterService;

        public BatchConversionService(
            IImageLoadService imageLoadService,
            IImageProcessor imageProcessor,
            IIcoConverterService icoConverterService)
        {
            _imageLoadService = imageLoadService;
            _imageProcessor = imageProcessor;
            _icoConverterService = icoConverterService;
        }

        public async Task<BatchConvertResult> ConvertAsync(
            IEnumerable<string> filePaths,
            string outputFolder,
            List<Size> resolutions,
            int cornerRadius,
            CornerQuality quality,
            double targetDpi,
            int svgRenderMaxSize,
            CancellationToken cancellationToken,
            Action<string> log)
        {
            var total = 0;
            var success = 0;
            var failed = 0;

            foreach (var filePath in filePaths)
            {
                total++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!_imageLoadService.IsSupportedImageFile(filePath))
                    {
                        log($"跳过非图片文件: {filePath}");
                        failed++;
                        continue;
                    }

                    log($"批量处理: {filePath}");

                    var loadResult = _imageLoadService.LoadImage(filePath, targetDpi, svgRenderMaxSize);
                    var normalizedBitmap = loadResult.Image;
                    var imageToConvert = normalizedBitmap;

                    var maxRadius = Math.Max(0, Math.Min(normalizedBitmap.PixelWidth, normalizedBitmap.PixelHeight) / 2);
                    var radius = Math.Clamp(cornerRadius, 0, maxRadius);

                    if (radius > 0)
                    {
                        imageToConvert = await Task.Run(() => _imageProcessor.ApplyRoundedCorners(normalizedBitmap, radius, quality));
                    }

                    var outputName = Path.GetFileNameWithoutExtension(filePath) + ".ico";
                    var outputPath = Path.Combine(outputFolder, outputName);

                    await _icoConverterService.ConvertToIcoAsync(imageToConvert, outputPath, resolutions, cancellationToken);
                    log($"已输出: {outputPath}");
                    success++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    log($"批量处理失败: {filePath}，原因: {ex.Message}");
                }
            }

            return new BatchConvertResult(total, success, failed);
        }
    }
}