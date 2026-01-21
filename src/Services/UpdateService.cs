using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace IcoConverter.Services
{
    /// <summary>
    /// GitHub Release 版本信息。
    /// </summary>
    public sealed record UpdateInfo(string TagName, Version? Version);

    /// <summary>
    /// 更新检查与启动更新器的服务。
    /// </summary>
    public class UpdateService
    {
        private const string LatestReleaseUrl = "https://api.github.com/repos/fallssyj/IcoConverter/releases/latest";
        private const string UpdateExecutableName = "Updagee.exe";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        /// <summary>
        /// 获取 GitHub 最新 Release 信息。
        /// </summary>
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

            // 兼容形如 v1.2.3 的标签格式
            var normalized = tagName.TrimStart('v', 'V');
            var version = Version.TryParse(normalized, out var parsed) ? parsed : null;
            return new UpdateInfo(tagName, version);
        }

        /// <summary>
        /// 启动外部更新器并退出当前程序。
        /// </summary>
        public void LaunchUpdate(string url, string appName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    CustomMessageBox.Show("更新地址无效。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrWhiteSpace(appName))
                {
                    CustomMessageBox.Show("应用名称无效。", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string updateExePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    UpdateExecutableName);

                if (!File.Exists(updateExePath))
                {
                    CustomMessageBox.Show($"更新程序 {UpdateExecutableName} 不存在！", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 参数约定由更新器解析
                string arguments = $"url={url} app={appName}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateExePath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(updateExePath),
                    UseShellExecute = false,
                    CreateNoWindow = false
                });
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"启动失败: {ex.Message}", "错误",
                              MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 创建带基础请求头的 <see cref="HttpClient"/>。
        /// </summary>
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
