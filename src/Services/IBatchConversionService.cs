using IcoConverter.ViewModels;
using System.Drawing;

namespace IcoConverter.Services
{
    /// <summary>
    /// 批量转换服务接口。
    /// </summary>
    public interface IBatchConversionService
    {
        /// <summary>
        /// 执行批量转换并返回统计结果。
        /// </summary>
        Task<BatchConvertResult> ConvertAsync(
            IEnumerable<string> filePaths,
            string outputFolder,
            List<Size> resolutions,
            int cornerRadius,
            CornerQuality quality,
            double targetDpi,
            int svgRenderMaxSize,
            CancellationToken cancellationToken,
            Action<string> log);
    }
}