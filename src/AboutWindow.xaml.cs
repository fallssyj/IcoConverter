using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;

namespace IcoConverter;

/// <summary>
/// 关于窗口
/// </summary>
public partial class AboutWindow
{
    public string _Title { get; set; } = "关于 IcoConverter";
    public ObservableCollection<string> Libraries { get; } =
    [
        "MiSans",
        "HandyControl",
        "Microsoft.Extensions.DependencyInjection",
        "System.Drawing.Common",
        "SkiaSharp",
        "Svg.Skia"
    ];
    public string LicenseText { get; set; } = "";
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
        init();
    }
    private void init()
    {
        GetVersion();
        LicenseText = LoadLicenseText();
    }
    private void GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            _Title += $" v{version.Major}.{version.Minor}.{version.Build}";
        }
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
