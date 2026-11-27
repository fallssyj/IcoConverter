using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace IcoConverter;

public partial class MainWindow
{
    private const int WmNcCalcSize = 0x0083;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseMove = 0x00A0;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtTop = 12;
    private const uint RefreshFrame = 0x0020 | 0x0010 | 0x0004 | 0x0002 | 0x0001;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(handle) is { } source)
        {
            source.AddHook(HandleTitleBarMessage);
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, RefreshFrame);
        }
    }

    private IntPtr HandleTitleBarMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmNcCalcSize)
        {
            // 保留系统计算的左右和底部边框，只将客户区向上延伸到标题栏。
            var frame = Marshal.PtrToStructure<NativeRect>(lParam);
            DefWindowProc(handle, message, wParam, lParam);
            var client = Marshal.PtrToStructure<NativeRect>(lParam);
            // 保留在 WPF 布局中的顶部留白，避免最大化时非客户区影响 DWM 按钮命中。
            var topInset = IsZoomed(handle) ? Math.Max(0, client.Left - frame.Left) : 0;
            var dpi = GetDpiForWindow(handle);
            if (WindowContent is { } content && dpi != 0)
            {
                content.Margin = new Thickness(0, topInset * 96d / dpi, 0, 0);
            }

            client.Top = frame.Top;
            Marshal.StructureToPtr(client, lParam, false);
            handled = true;
            return IntPtr.Zero;
        }

        if (message is WmNcHitTest or WmNcMouseMove or WmNcMouseLeave &&
            ReferenceEquals(Background, Brushes.Transparent) &&
            DwmDefWindowProc(handle, message, wParam, lParam, out var result))
        {
            handled = true;
            return result;
        }

        // 窗口已加载时，内容仍可能尚未接入当前窗口的显示源。
        if (message != WmNcHitTest || !IsLoaded || Content is not FrameworkElement clientArea ||
            PresentationSource.FromVisual(clientArea) is not HwndSource { IsDisposed: false, CompositionTarget: not null } source ||
            source.Handle != handle)
        {
            return IntPtr.Zero;
        }

        var coordinates = lParam.ToInt64();
        var point = clientArea.PointFromScreen(new Point((short)coordinates, (short)(coordinates >> 16)));
        if (point.X < 0 || point.X >= clientArea.ActualWidth || point.Y < 0 || point.Y >= TitleBarRow.ActualHeight)
        {
            return IntPtr.Zero;
        }

        handled = true;
        if (clientArea.InputHitTest(point) is { } element && WindowChrome.GetIsHitTestVisibleInChrome(element))
        {
            return (IntPtr)HtClient;
        }

        return WindowState == WindowState.Normal && point.Y < SystemParameters.WindowResizeBorderThickness.Top
            ? (IntPtr)HtTop
            : (IntPtr)HtCaption;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DwmDefWindowProc(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, out IntPtr result);
}
