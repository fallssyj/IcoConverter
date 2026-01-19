using IcoConverter.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace IcoConverter
{
    /// <summary>
    /// 自定义消息框窗口
    /// </summary>
    /// <remarks>
    /// 提供比标准MessageBox更美观和可定制的消息框
    /// </remarks>
    public partial class CustomMessageBox
    {
        private readonly CustomMessageBoxViewModel _viewModel;
        private MessageBoxResult _result = MessageBoxResult.None;

        /// <summary>
        /// 初始化CustomMessageBox的新实例
        /// </summary>
        public CustomMessageBox()
        {
            InitializeComponent();
            _viewModel = new CustomMessageBoxViewModel(OnCloseRequested);
            DataContext = _viewModel;
        }

        /// <summary>
        /// 显示自定义消息框
        /// </summary>
        /// <param name="message">要显示的消息内容</param>
        /// <param name="title">消息框标题（默认为"提示"）</param>
        /// <param name="icon">消息图标类型（默认为Information）</param>
        /// <param name="owner">消息框的所有者窗口，如果为null则自动检测当前活动窗口</param>
        public static MessageBoxResult Show(string message, string title = "提示", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.Information, Window? owner = null)
        {
            var dialog = new CustomMessageBox
            {
                Owner = owner ?? GetActiveWindow()
            };

            dialog._viewModel.Initialize(message, title, buttons, icon);

            dialog.ShowDialog();
            return dialog._result;
        }

        /// <summary>
        /// 显示自定义消息框（带按钮）
        /// </summary>
        /// <param name="message">要显示的消息内容</param>
        /// <param name="title">消息框标题</param>
        /// <param name="buttons">按钮类型</param>
        /// <param name="icon">消息图标类型（默认为Information）</param>
        /// <param name="owner">消息框的所有者窗口，如果为null则自动检测当前活动窗口</param>
        /// <returns>用户点击的按钮结果</returns>
        /// <remarks>
        /// 注意：当前简化版本只支持OK按钮，返回MessageBoxResult.OK
        /// 如果需要支持更多按钮类型，可以扩展这个实现
        /// </remarks>
        // 已使用单一带默认参数的 Show 方法实现所有重载需求
        // 兼容旧的调用顺序：Show(message, title, icon, owner)
        public static MessageBoxResult Show(string message, string title = "提示", MessageBoxImage icon = MessageBoxImage.Information, Window? owner = null)
        {
            return Show(message, title, MessageBoxButton.OK, icon, owner);
        }

        /// <summary>
        /// 获取当前活动窗口
        /// </summary>
        /// <returns>当前活动窗口，如果没有则返回主窗口</returns>
        private static Window GetActiveWindow()
        {
            // 首先尝试获取当前活动窗口
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

            // 如果没有活动窗口，尝试获取具有焦点的窗口
            if (activeWindow == null)
            {
                activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsFocused);
            }

            // 如果还是没有，使用主窗口作为后备
            return activeWindow ?? System.Windows.Application.Current.MainWindow;
        }

        /// <summary>
        /// 窗口鼠标左键按下事件处理，支持拖动窗口
        /// </summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_SizeChanged(object sender, RoutedEventArgs e)
        {
            if (Owner is null)
            {
                return;
            }

            var ownerSize = new System.Windows.Size(Owner.ActualWidth, Owner.ActualHeight);
            if (ownerSize.Width <= 0 || ownerSize.Height <= 0)
            {
                ownerSize = new System.Windows.Size(Owner.Width, Owner.Height);
            }

            var ownerTopLeft = Owner.PointToScreen(new System.Windows.Point(0, 0));

            var presentationSource = PresentationSource.FromVisual(this);
            if (presentationSource?.CompositionTarget is not null)
            {
                ownerTopLeft = presentationSource.CompositionTarget.TransformFromDevice.Transform(ownerTopLeft);
            }

            Left = ownerTopLeft.X + (ownerSize.Width - ActualWidth) / 2;
            Top = ownerTopLeft.Y + (ownerSize.Height - ActualHeight) / 2;
        }

        private void OnCloseRequested(MessageBoxResult result, bool? dialogResult)
        {
            _result = result;

            if (dialogResult.HasValue)
            {
                DialogResult = dialogResult.Value;
                return;
            }

            Close();
        }
    }
}
