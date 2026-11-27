using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services;

internal static class PngIconSetWriter
{
    internal static int Write(
        BitmapSource image,
        string folder,
        IEnumerable<(Size Size, string FileName)> outputs,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var original = BitmapExportHelper.ToBitmap(image);
        using var square = original.Width == original.Height ? null : BitmapExportHelper.FitToSquare(original);
        var source = square ?? original;
        var count = 0;

        foreach (var (size, fileName) in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var resized = BitmapExportHelper.Resize(source, size.Width, size.Height);
            File.WriteAllBytes(Path.Combine(folder, fileName), BitmapExportHelper.EncodePng(resized));
            count++;
        }

        return count;
    }
}
