using IcoConverter.Models;

namespace IcoConverter.Services
{
    /// <summary>把图标容器（ICO / ICNS）里的帧拆出来导成 PNG。</summary>
    public interface IIconPngConversionService
    {
        /// <summary>容器里包含的帧尺寸，已去重并按升序排列。</summary>
        IReadOnlyList<IcoResolution> GetResolutions(string iconPath);

        /// <summary>把选中的帧导出到目录，文件名形如 <c>name_256x256.png</c>。</summary>
        Task ExportToPngAsync(
            string iconPath,
            string outputDirectory,
            IEnumerable<IcoResolution> selectedResolutions,
            CancellationToken cancellationToken = default);
    }
}
