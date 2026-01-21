using IcoConverter.Models;

namespace IcoConverter.Services
{
    /// <summary>
    /// ICO 转 PNG 服务接口。
    /// </summary>
    public interface IIcoPngConversionService
    {
        /// <summary>
        /// 读取 ICO 文件中包含的分辨率列表。
        /// </summary>
        /// <param name="icoPath">ICO 文件路径。</param>
        /// <returns>分辨率集合。</returns>
        IReadOnlyList<IcoResolution> GetResolutions(string icoPath);

        /// <summary>
        /// 按选择的分辨率导出 PNG 文件。
        /// </summary>
        /// <param name="icoPath">ICO 文件路径。</param>
        /// <param name="outputDirectory">输出目录。</param>
        /// <param name="selectedResolutions">用户选择的分辨率集合。</param>
        /// <param name="cancellationToken">取消标记。</param>
        Task ExportToPngAsync(string icoPath, string outputDirectory, IEnumerable<IcoResolution> selectedResolutions, CancellationToken cancellationToken = default);
    }
}
