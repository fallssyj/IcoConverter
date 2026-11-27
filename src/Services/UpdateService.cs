using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace IcoConverter.Services
{
    public sealed record UpdateInfo(string TagName, Version? Version);

    public class UpdateService
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/fallssyj/IcoConverter/releases/latest";
        private const string UpdateExecutableName = "Updagee.exe";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public async Task<UpdateInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!json.RootElement.TryGetProperty("tag_name", out var tagProperty))
            {
                return null;
            }

            var tagName = tagProperty.GetString();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var normalized = tagName.TrimStart('v', 'V');
            var version = Version.TryParse(normalized, out var parsed) ? parsed : null;
            return new UpdateInfo(tagName, version);
        }

        public void LaunchUpdate(string url, string appName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    AppMessageBox.Show("更新地址无效。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(appName))
                {
                    AppMessageBox.Show("应用名称无效。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string updateExePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    UpdateExecutableName);

                if (!File.Exists(updateExePath))
                {
                    AppMessageBox.Show($"更新程序 {UpdateExecutableName} 不存在！", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = updateExePath,
                    WorkingDirectory = Path.GetDirectoryName(updateExePath),
                    UseShellExecute = false,
                    CreateNoWindow = false
                };

                // 逐个传递参数，由 ArgumentList 保留路径及应用名称中的空格。
                startInfo.ArgumentList.Add($"url={url}");
                startInfo.ArgumentList.Add($"app={appName}");

                Process.Start(startInfo);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                AppMessageBox.Show($"启动失败: {ex.Message}", "错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("IcoConverter/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
    }
}

