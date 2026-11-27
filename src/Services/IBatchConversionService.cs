using System.Drawing;
using IcoConverter.Models;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace IcoConverter.Services
{
    public interface IBatchConversionService
    {
        Task<BatchConvertResult> ConvertAsync(
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
            Action<string> log);
    }
}