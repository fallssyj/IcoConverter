using System.Drawing;
using System.IO;
using IcoConverter.Models;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace IcoConverter.Services
{
    // 单张失败不中断后续处理，结束后汇总结果
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
            RadiusUnit radiusUnit,
            MaskShape maskShape,
            CircleFitMode circleFitMode,
            int circlePaddingPercent,
            bool enableEdgeSupersampling,
            int canvasExpandPixels,
            MediaColor canvasFillColor,
            bool fillCanvasOutsideMask,
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

                    var radius = ResolveRadius(
                        normalizedBitmap.PixelWidth,
                        normalizedBitmap.PixelHeight,
                        cornerRadius,
                        radiusUnit,
                        maskShape,
                        circleFitMode,
                        circlePaddingPercent);

                    var renderOptions = new ImageRenderOptions(
                        maskShape,
                        radius,
                        ImageTransformOptions.Identity,
                        circleFitMode,
                        circlePaddingPercent,
                        enableEdgeSupersampling,
                        canvasExpandPixels,
                        canvasFillColor,
                        fillCanvasOutsideMask);
                    if (renderOptions.RequiresProcessing)
                    {
                        imageToConvert = await Task.Run(() => _imageProcessor.ApplyMask(normalizedBitmap, renderOptions));
                    }

                    var outputName = Path.GetFileNameWithoutExtension(filePath) + ".ico";
                    var outputPath = Path.Combine(outputFolder, outputName);

                    var writtenPath = await _icoConverterService.ConvertToUniqueIcoAsync(
                        imageToConvert, outputPath, resolutions, cancellationToken);
                    log($"已输出: {writtenPath}");
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

        private static int ResolveRadius(
            int width,
            int height,
            int radiusValue,
            RadiusUnit radiusUnit,
            MaskShape shape,
            CircleFitMode circleFitMode,
            int circlePaddingPercent)
        {
            var shortestHalf = Math.Max(0, Math.Min(width, height) / 2);

            if (shape == MaskShape.Circle)
            {
                return circleFitMode switch
                {
                    CircleFitMode.Circumscribed => Math.Max(0, Math.Max(width, height) / 2),
                    CircleFitMode.Padded => (int)Math.Round(shortestHalf * (1d - Math.Clamp(circlePaddingPercent, 0, 45) / 100d), MidpointRounding.AwayFromZero),
                    _ => shortestHalf
                };
            }

            if (radiusUnit == RadiusUnit.Percent)
            {
                var percent = Math.Clamp(radiusValue, 0, 100);
                return (int)Math.Round(shortestHalf * (percent / 100d), MidpointRounding.AwayFromZero);
            }

            return Math.Clamp(radiusValue, 0, shortestHalf);
        }
    }
}
