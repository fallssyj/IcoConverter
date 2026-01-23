using IcoConverter.Models;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public interface IIcoConverterService
    {
        Task ConvertToIcoAsync(BitmapSource image, string outputPath, List<Size> resolutions, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ExecutableIconCandidate>> ScanExecutableIconsAsync(string binaryPath, CancellationToken cancellationToken = default);
    }
}
