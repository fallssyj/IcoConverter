using IcoConverter.Services;
using IcoConverter.Utils;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace IcoConverter;

/// <summary>
/// 关于窗口
/// </summary>
public partial class AboutWindow
{
    /// <summary>
    /// 更新检查服务。
    /// </summary>
    private readonly UpdateService _updateService = new();

    /// <summary>
    /// 关于窗口标题（绑定到界面）。
    /// </summary>
    public string _Title { get; set; } = "关于 IcoConverter";

    /// <summary>
    /// 检查更新命令。
    /// </summary>
    public AsyncRelayCommand CheckUpdateCommand { get; }

    /// <summary>
    /// 依赖库展示列表。
    /// </summary>
    public ObservableCollection<string> Libraries { get; } =
    [
        "MiSans",
        "HandyControl",
        "Microsoft.Extensions.DependencyInjection",
        "System.Drawing.Common",
        "SkiaSharp",
        "Svg.Skia"
    ];

    /// <summary>
    /// 许可证文本内容。
    /// </summary>
    public string LicenseText { get; set; } = "";

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync);
        GetVersion();
        LicenseText = LoadLicenseText();
    }
    /// <summary>
    /// 检查最新版本并提示用户是否下载。
    /// </summary>
    private async Task CheckUpdateAsync()
    {
        try
        {
            var (currentVersion, currentVersionText) = GetCurrentVersionInfo();

            var processArch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();

            var latestInfo = await _updateService.GetLatestReleaseAsync();
            if (latestInfo is null)
            {
                CustomMessageBox.Show("无法获取最新版本信息。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            if (latestInfo.Version is null)
            {
                CustomMessageBox.Show($"最新版本号格式无效: {latestInfo.TagName}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            var latestVersion = latestInfo.Version;

            if (currentVersion is null)
            {
                CustomMessageBox.Show($"最新版本: {latestVersion}（当前: {currentVersionText}，架构: {processArch}）", "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Information, this);
                return;
            }

            var currentComparable = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
            if (latestVersion > currentComparable)
            {
                var result = CustomMessageBox.Show($"发现新版本: {latestVersion}\n当前版本: {currentVersionText}\n架构: {processArch}\n是否立即下载？", "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Information, this);
                if (result == MessageBoxResult.Yes)
                {
                    var downloadUrl = BuildDownloadUrl(latestInfo.TagName, processArch);
                    _updateService.LaunchUpdate(downloadUrl, "IcoConverter.exe");
                }
            }
            else
            {
                CustomMessageBox.Show($"已是最新版本: {currentVersionText}\n架构: {processArch}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
        }
        catch (HttpRequestException ex)
        {
            CustomMessageBox.Show($"检查更新失败: {ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
        catch (Exception ex)
        {
            CustomMessageBox.Show($"发生未知错误: {ex.Message}", "检查更新", MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }
    /// <summary>
    /// 读取程序集版本并拼接标题。
    /// </summary>
    private void GetVersion()
    {
        var (_, currentVersionText) = GetCurrentVersionInfo();
        if (currentVersionText != "未知")
        {
            _Title += $" v{currentVersionText}";
        }
    }

    /// <summary>
    /// 获取当前程序集版本及其显示文本。
    /// </summary>
    private static (Version? Version, string Text) GetCurrentVersionInfo()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var text = version is null
            ? "未知"
            : $"{version.Major}.{version.Minor}.{version.Build}";
        return (version, text);
    }

    /// <summary>
    /// 生成更新包下载地址。
    /// </summary>
    private static string BuildDownloadUrl(string tagName, string processArch)
    {
        return $"https://github.com/fallssyj/IcoConverter/releases/download/{tagName}/IcoConverter-win-{processArch}.zip";
    }

    /// <summary>
    /// 从资源中读取许可证文本。
    /// </summary>
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
