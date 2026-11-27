using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;

namespace IcoConverter.Services
{
    public sealed class WebsiteIconExportService(IIcoConverterService icoConverterService)
    {
        private const string FolderName = "website-icons";

        private static readonly (Size Size, string FileName)[] PngIcons =
        [
            (new Size(16, 16), "favicon-16x16.png"),
            (new Size(32, 32), "favicon-32x32.png"),
            (new Size(180, 180), "apple-touch-icon.png")
        ];

        public async Task<string> ExportAsync(
            BitmapSource image,
            string outputFolder,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stagingFolder = Path.Combine(outputFolder, $".{FolderName}-{Guid.NewGuid():N}.tmp");
            Directory.CreateDirectory(stagingFolder);

            try
            {
                await icoConverterService.ConvertToIcoAsync(
                    image,
                    Path.Combine(stagingFolder, "favicon.ico"),
                    [new Size(16, 16), new Size(32, 32), new Size(48, 48)],
                    cancellationToken).ConfigureAwait(false);

                return await Task.Run(() =>
                {
                    PngIconSetWriter.Write(image, stagingFolder, PngIcons, cancellationToken);
                    return MoveToAvailableFolder(stagingFolder, outputFolder, cancellationToken);
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(stagingFolder))
                    {
                        Directory.Delete(stagingFolder, recursive: true);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 清理失败时保留导出异常。
                }
            }
        }

        // 整组图标生成后再移动目录，避免留下半套输出或覆盖已有图标。
        private static string MoveToAvailableFolder(
            string stagingFolder,
            string outputFolder,
            CancellationToken cancellationToken)
        {
            var folder = Path.Combine(outputFolder, FolderName);
            for (var suffix = 2; ; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    Directory.Move(stagingFolder, folder);
                    return folder;
                }
                catch (IOException) when (Directory.Exists(folder) || File.Exists(folder))
                {
                    folder = Path.Combine(outputFolder, $"{FolderName} ({suffix})");
                }
            }
        }
    }
}
