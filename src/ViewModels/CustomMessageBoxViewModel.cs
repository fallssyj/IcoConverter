using IcoConverter.Utils;
using System.Windows;
using System.Windows.Input;

namespace IcoConverter.ViewModels
{
    /// <summary>
    /// 自定义消息框的视图模型，负责按钮显示、图标样式和关闭回调。
    /// </summary>
    public class CustomMessageBoxViewModel : ViewModelBase
    {
        private readonly Action<MessageBoxResult, bool?> _closeAction;

        private string _msgTitle = "提示";
        private string _message = string.Empty;
        private string _iconText = "i";
        private string _iconBackground = "#007ACC";
        private Visibility _iconVisibility = Visibility.Visible;
        private Visibility _okButtonVisibility = Visibility.Visible;
        private Visibility _yesButtonVisibility = Visibility.Collapsed;
        private Visibility _noButtonVisibility = Visibility.Collapsed;
        private Visibility _cancelButtonVisibility = Visibility.Collapsed;

        /// <summary>
        /// 初始化消息框 ViewModel，注册按钮命令回调。
        /// </summary>
        public CustomMessageBoxViewModel(Action<MessageBoxResult, bool?> closeAction)
        {
            _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));

            OkCommand = new RelayCommand(_ => Close(MessageBoxResult.OK, true));
            YesCommand = new RelayCommand(_ => Close(MessageBoxResult.Yes, true));
            NoCommand = new RelayCommand(_ => Close(MessageBoxResult.No, false));
            CancelCommand = new RelayCommand(_ => Close(MessageBoxResult.Cancel, false));
        }

        /// <summary>
        /// 消息框标题文本。
        /// </summary>
        public string MsgTitle
        {
            get => _msgTitle;
            private set => SetField(ref _msgTitle, value);
        }

        /// <summary>
        /// 主体提示内容。
        /// </summary>
        public string Message
        {
            get => _message;
            private set => SetField(ref _message, value);
        }

        /// <summary>
        /// 图标字符，用于显示不同类型的信息。
        /// </summary>
        public string IconText
        {
            get => _iconText;
            private set => SetField(ref _iconText, value);
        }

        /// <summary>
        /// 图标背景颜色（字符串格式）。
        /// </summary>
        public string IconBackground
        {
            get => _iconBackground;
            private set => SetField(ref _iconBackground, value);
        }

        /// <summary>
        /// 图标可见性，用于无图标场景。
        /// </summary>
        public Visibility IconVisibility
        {
            get => _iconVisibility;
            private set => SetField(ref _iconVisibility, value);
        }

        /// <summary>
        /// OK 按钮的可见性。
        /// </summary>
        public Visibility OkButtonVisibility
        {
            get => _okButtonVisibility;
            private set => SetField(ref _okButtonVisibility, value);
        }

        /// <summary>
        /// Yes 按钮的可见性。
        /// </summary>
        public Visibility YesButtonVisibility
        {
            get => _yesButtonVisibility;
            private set => SetField(ref _yesButtonVisibility, value);
        }

        /// <summary>
        /// No 按钮的可见性。
        /// </summary>
        public Visibility NoButtonVisibility
        {
            get => _noButtonVisibility;
            private set => SetField(ref _noButtonVisibility, value);
        }

        /// <summary>
        /// Cancel 按钮的可见性。
        /// </summary>
        public Visibility CancelButtonVisibility
        {
            get => _cancelButtonVisibility;
            private set => SetField(ref _cancelButtonVisibility, value);
        }

        /// <summary>
        /// OK 按钮命令。
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Yes 按钮命令。
        /// </summary>
        public ICommand YesCommand { get; }

        /// <summary>
        /// No 按钮命令。
        /// </summary>
        public ICommand NoCommand { get; }

        /// <summary>
        /// Cancel 按钮命令。
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// 根据输入初始化消息文本、标题、按钮和图标。
        /// </summary>
        public void Initialize(string message, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            Message = message;
            MsgTitle = title;
            ApplyIconStyle(icon);
            ConfigureButtons(buttons);
        }

        /// <summary>
        /// 根据按钮枚举决定显示哪些操作按钮。
        /// </summary>
        private void ConfigureButtons(MessageBoxButton buttons)
        {
            OkButtonVisibility = Visibility.Collapsed;
            YesButtonVisibility = Visibility.Collapsed;
            NoButtonVisibility = Visibility.Collapsed;
            CancelButtonVisibility = Visibility.Collapsed;

            switch (buttons)
            {
                case MessageBoxButton.OK:
                    OkButtonVisibility = Visibility.Visible;
                    break;
                case MessageBoxButton.OKCancel:
                    OkButtonVisibility = Visibility.Visible;
                    CancelButtonVisibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNo:
                    YesButtonVisibility = Visibility.Visible;
                    NoButtonVisibility = Visibility.Visible;
                    break;
                case MessageBoxButton.YesNoCancel:
                    YesButtonVisibility = Visibility.Visible;
                    NoButtonVisibility = Visibility.Visible;
                    CancelButtonVisibility = Visibility.Visible;
                    break;
                default:
                    OkButtonVisibility = Visibility.Visible;
                    break;
            }
        }

        /// <summary>
        /// 根据图标类型设置图标文本、背景色及显示状态。
        /// </summary>
        private void ApplyIconStyle(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error:
                    IconText = "!";
                    IconBackground = "#FF5555";
                    IconVisibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Warning:
                    IconText = "!";
                    IconBackground = "#FFA500";
                    IconVisibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Question:
                    IconText = "?";
                    IconBackground = "#4CAF50";
                    IconVisibility = Visibility.Visible;
                    break;
                case MessageBoxImage.Information:
                    IconText = "i";
                    IconBackground = "#007ACC";
                    IconVisibility = Visibility.Visible;
                    break;
                default:
                    IconVisibility = Visibility.Collapsed;
                    break;
            }
        }

        /// <summary>
        /// 通知宿主窗口关闭并返回用户选择。
        /// </summary>
        private void Close(MessageBoxResult result, bool? dialogResult)
        {
            _closeAction(result, dialogResult);
        }
    }
}
