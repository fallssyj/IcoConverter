using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public interface IIcoConverterService
    {
        Task ConvertToIcoAsync(BitmapSource image, string outputPath, List<Size> resolutions, CancellationToken cancellationToken = default);
    }
}
