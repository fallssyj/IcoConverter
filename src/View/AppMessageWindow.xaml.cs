using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace IcoConverter;

/// <summary>
/// 随应用主题显示的提示窗口。
/// 请通过 <see cref="Services.AppMessageBox"/> 显示此窗口，以保留系统提示框回退逻辑。
/// </summary>
public partial class AppMessageWindow : Window
{
    private const int ExtendedStyleIndex = -20;
    private const int DialogModalFrame = 0x0001;
    private const uint SetIconMessage = 0x0080;
    private const uint FrameRefreshFlags = 0x0020 | 0x0010 | 0x0004 | 0x0002 | 0x0001;

    private const double MinWindowWidth = 260;
    private const double MaxWindowWidth = 480;

    // 正文左右边距、图标区与图标文字间距之和，与 XAML 中的取值对应
    private const double HorizontalChrome = 20 + 32 + 14 + 20;

    private const double ButtonWidth = 88;
    private const double ButtonGap = 8;

    private MessageBoxResult _result;
    private MessageBoxResult _fallbackResult = MessageBoxResult.OK;
    private int _buttonCount;
    private bool _hasResult;

    public MessageBoxResult Result => _result;

    public AppMessageWindow(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage icon)
    {
        InitializeComponent();

        Title = string.IsNullOrWhiteSpace(title) ? "提示" : title;

        HeaderText.Text = string.Equals(title, "提示", StringComparison.Ordinal) ? string.Empty : title;
        HeaderText.Visibility = string.IsNullOrEmpty(HeaderText.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        MessageText.Text = message ?? string.Empty;

        ApplyIcon(icon);
        BuildButtons(buttons);

        // 限制窗口高度不超过屏幕，超出部分在正文区内滚动
        MessageScroll.MaxHeight = Math.Max(160, SystemParameters.WorkArea.Height * 0.45);

        // 宽度取标题与正文中最长一行，避免短消息产生大片空白，同时需容纳按钮行
        var buttonRowWidth = _buttonCount * ButtonWidth + Math.Max(0, _buttonCount - 1) * ButtonGap;
        Width = Math.Clamp(
            Math.Max(
                MeasureContentWidth() + HorizontalChrome + 2,
                buttonRowWidth + 2 * 20 + 2),
            MinWindowWidth,
            MaxWindowWidth);

        Closing += OnWindowClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 使用系统对话框边框，避免标题栏回退到应用程序图标。
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, ExtendedStyleIndex);
        SetWindowLong(handle, ExtendedStyleIndex, style | DialogModalFrame);
        SendMessage(handle, SetIconMessage, IntPtr.Zero, IntPtr.Zero); // 小图标
        SendMessage(handle, SetIconMessage, new IntPtr(1), IntPtr.Zero); // 大图标
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, FrameRefreshFlags);
    }

    private void ApplyIcon(MessageBoxImage icon)
    {
        var (geometryKey, fillKey, backgroundKey) = icon switch
        {
            MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop =>
                ("IconError", "SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
            MessageBoxImage.Warning or MessageBoxImage.Exclamation =>
                ("IconWarning", "SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush"),
            // Question 复用信息图标。
            _ => ("IconInfo", "SystemFillColorAttentionBrush", "SystemFillColorAttentionBackgroundBrush"),
        };

        if (TryFindResource(geometryKey) is Geometry geometry)
        {
            IconPath.Data = geometry;
        }

        IconPath.SetResourceReference(Shape.FillProperty, fillKey);
        IconBackground.SetResourceReference(Border.BackgroundProperty, backgroundKey);
    }

    // 主按钮置于最右并使用强调色，取消按钮置于最左，与应用内其他对话框一致
    private void BuildButtons(MessageBoxButton buttons)
    {
        var specs = new List<ButtonSpec>();
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                specs.Add(new ButtonSpec(MessageBoxResult.Cancel, "取消"));
                specs.Add(new ButtonSpec(MessageBoxResult.OK, "确定", IsPrimary: true));
                break;
            case MessageBoxButton.YesNo:
                specs.Add(new ButtonSpec(MessageBoxResult.No, "否"));
                specs.Add(new ButtonSpec(MessageBoxResult.Yes, "是", IsPrimary: true));
                break;
            case MessageBoxButton.YesNoCancel:
                specs.Add(new ButtonSpec(MessageBoxResult.Cancel, "取消"));
                specs.Add(new ButtonSpec(MessageBoxResult.No, "否"));
                specs.Add(new ButtonSpec(MessageBoxResult.Yes, "是", IsPrimary: true));
                break;
            default:
                specs.Add(new ButtonSpec(MessageBoxResult.OK, "确定", IsPrimary: true));
                break;
        }

        var primaryStyle = TryFindResource("AccentButtonStyle") as Style;
        _buttonCount = specs.Count;

        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var button = new Button
            {
                Content = spec.Text,
                Width = ButtonWidth,
                Height = 32,
                Margin = new Thickness(0, 0, i == specs.Count - 1 ? 0 : ButtonGap, 0),
                Style = spec.IsPrimary ? primaryStyle : null,
                // Enter 触发主按钮。Esc 由标记为取消按钮的「取消」或「确定」处理。
                IsDefault = spec.IsPrimary,
                IsCancel = spec.Result is MessageBoxResult.Cancel or MessageBoxResult.OK,
            };

            button.Click += (_, _) => Complete(spec.Result);
            ButtonPanel.Children.Add(button);
        }

        // 用户通过标题栏或 Alt+F4 关闭窗口时，按按钮组合给出默认结果。
        _fallbackResult = buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK,
        };
    }

    private void Complete(MessageBoxResult result)
    {
        _result = result;
        _hasResult = true;

        try
        {
            // 模态时走 DialogResult，顺带解除 ShowDialog 的阻塞
            DialogResult = true;
        }
        catch (InvalidOperationException)
        {
            // 非模态调用没有 DialogResult 可用
            Close();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_hasResult)
        {
            return;
        }

        // 标题栏关闭 / Alt+F4 时返回安全默认值，避免调用方收到 None
        _result = _fallbackResult;
        _hasResult = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.C || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        try
        {
            Clipboard.SetText(MessageText.Text);
        }
        catch
        {
            // 剪贴板写入失败时继续显示当前对话框。
        }

        e.Handled = true;
    }

    private double MeasureContentWidth()
    {
        var titleWidth = HeaderText.Visibility == Visibility.Visible
            ? MeasureWidestLine(HeaderText.Text, HeaderText)
            : 0;

        return Math.Max(titleWidth, MeasureWidestLine(MessageText.Text, MessageText));
    }

    private static double MeasureWidestLine(string text, System.Windows.Controls.TextBlock reference)
    {
        var typeface = new Typeface(
            reference.FontFamily,
            reference.FontStyle,
            reference.FontWeight,
            reference.FontStretch);

        var max = 0d;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var formatted = new FormattedText(
                line.Length == 0 ? " " : line,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                reference.FontSize,
                Brushes.Black,
                1.0);

            max = Math.Max(max, formatted.WidthIncludingTrailingWhitespace);
        }

        return max;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private readonly record struct ButtonSpec(
        MessageBoxResult Result,
        string Text,
        bool IsPrimary = false);
}
