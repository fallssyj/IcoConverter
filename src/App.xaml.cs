using IcoConverter.Services;
using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace IcoConverter;

public partial class App : System.Windows.Application
{
    // 每次运行最多为前三次未处理的 UI 线程异常显示提示。
    private const int MaxCrashDialogs = 3;
    private static int _crashDialogCount;

    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);

        // 指定 --out 时进入命令行模式：不创建窗口与容器，处理完成后退出
        if (CliRunner.IsCliInvocation(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(CliRunner.Run(e.Args));
            return;
        }

        // 统一在 Window.Loaded 读取当前材质，新增对话框无需单独注册。
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAnyWindowLoaded));

        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;

        if (e.Args.Length > 0)
        {
            // 支持"用此程序打开"，首个参数作为文件路径
            var viewModel = ServiceProvider.GetRequiredService<MainViewModel>();
            viewModel.ProcessImageFile(e.Args[0]);
        }

        mainWindow.Show();

    }

    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            WindowMaterialService.Apply(window, WindowMaterialService.Current);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            ServiceProvider.GetService<MainViewModel>()?.FlushPendingSettings();
            (ServiceProvider as IDisposable)?.Dispose();
        }
        catch
        {
            // 清理失败时继续完成退出。
        }

        base.OnExit(e);
    }

    // 记录每次未处理的 UI 线程异常，并在提示次数上限内显示摘要。
    // 前 3 次标记为已处理以维持运行，之后不再拦截，交由系统处理
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = CrashLog.Write(e.Exception, "Dispatcher");
        MirrorToAppLog(e.Exception);
        Debug.WriteLine($"[Crash] UI 线程未处理异常: {e.Exception}");

        var showDialog = _crashDialogCount < MaxCrashDialogs;
        e.Handled = showDialog;

        if (!showDialog)
        {
            return;
        }

        _crashDialogCount++;

        // 崩溃兜底仍使用系统消息框：此时 UI 线程状态不可信，自绘窗口需读取主题资源
        // 并执行完整布局，二次失败概率高于系统实现
        System.Windows.MessageBox.Show(
            BuildCrashMessage(e.Exception, logPath),
            "IcoConverter 遇到错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    // 记录 AppDomain 报告的未处理异常和进程终止状态。
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            return;
        }

        var logPath = CrashLog.Write(exception, "AppDomain");
        MirrorToAppLog(exception);
        Debug.WriteLine($"[Crash] 未处理异常(terminating={e.IsTerminating}): {exception} -> {logPath}");
    }

    // 记录未观察的任务异常，并将其标记为已观察。
    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var logPath = CrashLog.Write(e.Exception, "Task");
        MirrorToAppLog(e.Exception);
        Debug.WriteLine($"[Crash] 未观察的 Task 异常: {e.Exception} -> {logPath}");
        e.SetObserved();
    }

    private static void MirrorToAppLog(Exception exception)
    {
        try
        {
            ServiceProvider?.GetService<ILogService>()?.Add(
                $"未处理异常: {exception.GetType().Name}: {exception.Message}");
        }
        catch
        {
            // 应用日志不可用时，保留已写入的崩溃日志。
        }
    }

    private static string BuildCrashMessage(Exception exception, string logPath)
    {
        var summary = $"{exception.GetType().Name}: {exception.Message}";
        if (summary.Length > 400)
        {
            summary = summary[..400] + "…";
        }

        var pathLine = string.IsNullOrEmpty(logPath)
            ? "崩溃日志写入失败。"
            : $"详细信息已写入：{logPath}";

        return $"发生未处理的异常，当前操作可能已中断。\n\n{summary}\n\n{pathLine}";
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IImageProcessor, ImageProcessor>();
        services.AddSingleton<IIcoConverterService, IcoConverterService>();
        services.AddSingleton<ExecutableIconReader>();
        services.AddSingleton<WebsiteIconExportService>();
        services.AddSingleton<IIcnsConverterService, IcnsConverterService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IImageLoadService, ImageLoadService>();
        services.AddSingleton<IBatchConversionService, BatchConversionService>();
        services.AddSingleton<IIconPngConversionService, IconPngConversionService>();
        services.AddSingleton<UpdateService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<IcoToPngViewModel>();
        services.AddTransient<ExecutableIconPickerViewModel>();

        services.AddSingleton<MainWindow>();
        services.AddTransient<AboutWindow>();
        services.AddTransient<IcoToPngWindow>();
        services.AddTransient<ExecutableIconPickerWindow>();
    }
}
