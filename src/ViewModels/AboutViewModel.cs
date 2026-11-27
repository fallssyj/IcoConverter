using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IcoConverter.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace IcoConverter.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    private readonly UpdateService _updateService;

    [ObservableProperty]
    private string _title = "关于 IcoConverter";

    [ObservableProperty]
    private string _licenseText = string.Empty;

    public string Subtitle { get; }

    public ObservableCollection<LibraryCredit> Libraries { get; } =
    [
        new("fluentui-system-icons", "界面图标"),
        new(".NET WPF Fluent", "Windows 11 风格主题"),
        new("CommunityToolkit.Mvvm", "MVVM 与命令"),
        new("Microsoft.Extensions.DependencyInjection", "依赖注入"),
        new("SkiaSharp", "位图渲染与编码"),
        new("Svg.Skia", "SVG 渲染")
    ];

    public IAsyncRelayCommand CheckUpdateCommand { get; }

    public IRelayCommand OpenProjectPageCommand { get; }

    public AboutViewModel(UpdateService updateService)
    {
        _updateService = updateService;

        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync);
        OpenProjectPageCommand = new RelayCommand(OpenProjectPage);

        var (_, versionText) = GetCurrentVersionInfo();
        Subtitle = $"{versionText} · {RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        Title = BuildTitle();
        LicenseText = LoadLicenseText();
    }

    private static void OpenProjectPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/fallssyj/IcoConverter") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"无法打开浏览器: {ex.Message}", "打开链接", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task CheckUpdateAsync()
    {
        try
        {
            var (currentVersion, currentVersionText) = GetCurrentVersionInfo();
            var processArch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();

            var latestInfo = await _updateService.GetLatestReleaseAsync();
            if (latestInfo is null)
            {
                AppMessageBox.Show("无法获取最新版本信息。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (latestInfo.Version is null)
            {
                AppMessageBox.Show($"最新版本号格式无效: {latestInfo.TagName}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var latestVersion = latestInfo.Version;
            if (currentVersion is null)
            {
                AppMessageBox.Show($"最新版本: {latestVersion}（当前: {currentVersionText}，架构: {processArch}）", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var currentComparable = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
            if (latestVersion > currentComparable)
            {
                var result = AppMessageBox.Show(
                    $"发现新版本: {latestVersion}\n当前版本: {currentVersionText}\n架构: {processArch}\n是否立即下载？",
                    "检查更新",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    var downloadUrl = BuildDownloadUrl(latestInfo.TagName, processArch);
                    _updateService.LaunchUpdate(downloadUrl, "IcoConverter.exe");
                }
            }
            else
            {
                AppMessageBox.Show($"已是最新版本: {currentVersionText}\n架构: {processArch}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (HttpRequestException ex)
        {
            AppMessageBox.Show($"检查更新失败: {ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show($"发生未知错误: {ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string BuildTitle()
    {
        var (_, currentVersionText) = GetCurrentVersionInfo();
        return currentVersionText == "未知"
            ? "关于 IcoConverter"
            : $"关于 IcoConverter v{currentVersionText}";
    }

    private static (Version? Version, string Text) GetCurrentVersionInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var text = version is null
            ? "未知"
            : $"{version.Major}.{version.Minor}.{version.Build}";

        return (version, text);
    }

    private static string BuildDownloadUrl(string tagName, string processArch)
    {
        return $"https://github.com/fallssyj/IcoConverter/releases/download/{tagName}/IcoConverter-win-{processArch}.zip";
    }

    private static string LoadLicenseText()
    {
        try
        {
            var resourceUri = new Uri("Assets/LICENSE/LICENSE", UriKind.Relative);
            var streamInfo = System.Windows.Application.GetResourceStream(resourceUri);
            if (streamInfo?.Stream == null)
            {
                return "MIT License";
            }

            using var reader = new StreamReader(streamInfo.Stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        catch
        {
            return "MIT License";
        }
    }
}

public sealed record LibraryCredit(string Name, string Description);
