using IcoConverter.Models;
using System.IO;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services;

internal static class PreviewImageExporter
{
    internal static async Task WriteAsync(
        BitmapSource image,
        string path,
        PreviewExportFormat format,
        WebpExportOptions webpOptions)
    {
        using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

        if (format == PreviewExportFormat.Webp)
        {
            WebpExportHelper.Save(image, stream, webpOptions);
        }
        else
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
        }

        await stream.FlushAsync();
    }
}
