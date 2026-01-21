using IcoConverter.Services;
using IcoConverter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace IcoConverter;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static IServiceProvider ServiceProvider { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 配置依赖注入
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        if (e.Args.Length > 0)
        {
            // 如果有命令行参数，传递给ViewModel处理
            var viewModel = ServiceProvider.GetRequiredService<MainViewModel>();
            viewModel.ProcessImageFile(e.Args[0]);
        }

    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 注册服务
        services.AddSingleton<IImageProcessor, ImageProcessor>();
        services.AddSingleton<IIcoConverterService, IcoConverterService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IImageLoadService, ImageLoadService>();
        services.AddSingleton<IBatchConversionService, BatchConversionService>();
        services.AddSingleton<IIcoPngConversionService, IcoPngConversionService>();

        // 注册ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<IcoToPngViewModel>();

        // 注册Views
        services.AddSingleton<MainWindow>();
        services.AddTransient<IcoToPngWindow>();
    }
}
