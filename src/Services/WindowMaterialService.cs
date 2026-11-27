using IcoConverter.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace IcoConverter.Services
{
    /// <summary>
    /// 维护应用选中的窗口材质。窗口加载时应用当前值，
    /// 切换材质时更新 Application.Windows 中的窗口。
    /// </summary>
    public static class WindowMaterialService
    {
        private const int SystemBackdropAttribute = 38;
        private const int NoBackdrop = 1;
        private const int MicaBackdrop = 2;
        private const int AcrylicBackdrop = 3;

        /// <summary>当前选中的材质，启动时从设置里读出来。</summary>
        public static WindowMaterial Current { get; private set; } = WindowMaterial.Mica;

        public static void SetCurrent(WindowMaterial material) => Current = material;

        /// <summary>切换材质并立即应用到所有已打开的窗口（主窗口 + 正在显示的对话框）。</summary>
        public static void ApplyToAll(WindowMaterial material)
        {
            Current = material;

            var application = Application.Current;
            if (application is null)
            {
                return;
            }

            foreach (Window window in application.Windows)
            {
                Apply(window, material);
            }
        }

        public static void Apply(Window? window, WindowMaterial material)
        {
            if (window is null || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                return;
            }

            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero ||
                HwndSource.FromHwnd(handle) is not { CompositionTarget: { } target })
            {
                return;
            }

            var backdrop = SystemParameters.HighContrast ? NoBackdrop : material switch
            {
                WindowMaterial.Acrylic => AcrylicBackdrop,
                WindowMaterial.Solid => NoBackdrop,
                _ => MicaBackdrop
            };

            var result = DwmSetWindowAttribute(handle, SystemBackdropAttribute, in backdrop, sizeof(int));
            if (result >= 0 && backdrop != NoBackdrop)
            {
                var fullFrame = new Margins(-1);
                result = DwmExtendFrameIntoClientArea(handle, in fullFrame);
                if (result >= 0)
                {
                    target.BackgroundColor = Colors.Transparent;
                    window.Background = Brushes.Transparent;
                    return;
                }

                var none = NoBackdrop;
                DwmSetWindowAttribute(handle, SystemBackdropAttribute, in none, sizeof(int));
            }

            var noExtension = new Margins(0);
            DwmExtendFrameIntoClientArea(handle, in noExtension);
            target.BackgroundColor = SystemColors.WindowColor;
            window.ClearValue(Window.BackgroundProperty);
            if (result < 0)
            {
                Debug.WriteLine($"[WindowMaterial] 应用 {material} 失败: 0x{result:X8}");
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, in int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, in Margins margins);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Margins(int value)
        {
            private readonly int _left = value;
            private readonly int _right = value;
            private readonly int _top = value;
            private readonly int _bottom = value;
        }

        public static string GetDisplayName(WindowMaterial material) => material switch
        {
            WindowMaterial.Acrylic => "亚克力（Acrylic）",
            WindowMaterial.Solid => "纯色",
            _ => "云母（Mica）",
        };
    }
}
