using System.Drawing;
using System.Threading;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public sealed class IcnsConversionResult
    {
        public IcnsConversionResult(
            IReadOnlyList<Size> writtenSizes,
            IReadOnlyList<Size> skippedSizes,
            int entryCount,
            long fileSize)
        {
            WrittenSizes = writtenSizes;
            SkippedSizes = skippedSizes;
            EntryCount = entryCount;
            FileSize = fileSize;
        }

        /// <summary>真正写进文件的尺寸（已去重、按升序）。</summary>
        public IReadOnlyList<Size> WrittenSizes { get; }

        /// <summary>选了但 ICNS 没有对应类型、被跳过的尺寸。</summary>
        public IReadOnlyList<Size> SkippedSizes { get; }

        /// <summary>包含 Retina 别名的元素总数。</summary>
        public int EntryCount { get; }

        public long FileSize { get; }
    }

    public interface IIcnsConverterService
    {
        Task<IcnsConversionResult> ConvertToIcnsAsync(
            BitmapSource image,
            string outputPath,
            IReadOnlyList<Size> resolutions,
            CancellationToken cancellationToken = default);
    }
}
