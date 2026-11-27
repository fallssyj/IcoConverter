using System.Diagnostics;
using System.Windows;
using SystemSounds = System.Media.SystemSounds;

namespace IcoConverter.Services
{
    /// <summary>
    /// 应用内统一的提示框入口。使用自绘窗口以跟随主题与窗口材质，
    /// 仅在 UI 线程不可用或自绘初始化失败时回退到系统 MessageBox。
    /// </summary>
    public static class AppMessageBox
    {
        public static MessageBoxResult Show(
            string message,
            string title = "提示",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information,
            Window? owner = null)
        {
            var app = Application.Current;
            if (app?.Dispatcher is null)
            {
                return MessageBox.Show(message, title, buttons, icon);
            }

            if (!app.Dispatcher.CheckAccess())
            {
                return app.Dispatcher.Invoke(() => ShowCore(message, title, buttons, icon, owner));
            }

            return ShowCore(message, title, buttons, icon, owner);
        }

        public static Task<MessageBoxResult> ShowAsync(
            string message,
            string title = "提示",
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information,
            Window? owner = null)
        {
            var app = Application.Current;
            if (app?.Dispatcher is null)
            {
                return Task.FromResult(MessageBox.Show(message, title, buttons, icon));
            }

            return app.Dispatcher
                .InvokeAsync(() => ShowCore(message, title, buttons, icon, owner))
                .Task;
        }

        private static MessageBoxResult ShowCore(
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage icon,
            Window? owner)
        {
            PlaySystemSound(icon);

            // 尚未显示的窗口不能作为 Owner，否则 WPF 抛出异常
            var target = owner ?? GetActiveWindow();
            if (target is { IsVisible: false })
            {
                target = null;
            }

            var app = Application.Current;
            if (app is null || !app.Dispatcher.CheckAccess())
            {
                return ShowSystem(target, message, title, buttons, icon);
            }

            try
            {
                var dialog = new AppMessageWindow(message, title, buttons, icon)
                {
                    WindowStartupLocation = target is null
                        ? WindowStartupLocation.CenterScreen
                        : WindowStartupLocation.CenterOwner,
                    Owner = target,
                };

                dialog.ShowDialog();
                return dialog.Result;
            }
            catch (Exception ex)
            {
                // 主题资源缺失、XAML 解析失败等情况回退到系统对话框，保证提示仍然可见
                Debug.WriteLine($"[AppMessageBox] 自绘提示框失败，回退系统消息框: {ex}");
                return ShowSystem(target, message, title, buttons, icon);
            }
        }

        private static MessageBoxResult ShowSystem(
            Window? owner,
            string message,
            string title,
            MessageBoxButton buttons,
            MessageBoxImage icon)
        {
            return owner != null
                ? MessageBox.Show(owner, message, title, buttons, icon)
                : MessageBox.Show(message, title, buttons, icon);
        }

        private static Window? GetActiveWindow()
        {
            return Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow;
        }

        private static void PlaySystemSound(MessageBoxImage icon)
        {
            try
            {
                if (icon is MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop)
                {
                    SystemSounds.Hand.Play();
                    return;
                }

                if (icon is MessageBoxImage.Warning or MessageBoxImage.Exclamation)
                {
                    SystemSounds.Exclamation.Play();
                    return;
                }

                if (icon == MessageBoxImage.Question)
                {
                    SystemSounds.Question.Play();
                    return;
                }

                if (icon is MessageBoxImage.Information or MessageBoxImage.Asterisk)
                {
                    SystemSounds.Asterisk.Play();
                }
            }
            catch
            {
                // 提示音播放失败不影响对话框显示
            }
        }
    }
}
