using System.Windows;

namespace IcoConverter.Utils
{
    public static class DialogWindowBehavior
    {
        public static readonly DependencyProperty DialogResultProperty =
            DependencyProperty.RegisterAttached(
                "DialogResult",
                typeof(bool?),
                typeof(DialogWindowBehavior),
                new PropertyMetadata(null, OnDialogResultChanged));

        public static void SetDialogResult(DependencyObject element, bool? value)
            => element.SetValue(DialogResultProperty, value);

        public static bool? GetDialogResult(DependencyObject element)
            => (bool?)element.GetValue(DialogResultProperty);

        private static void OnDialogResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Window window || e.NewValue is not bool dialogResult)
            {
                return;
            }

            void CloseWindow()
            {
                if (!window.IsVisible)
                {
                    return;
                }

                try
                {
                    window.DialogResult = dialogResult;
                }
                catch
                {
                    window.Close();
                }
            }

            if (window.IsLoaded)
            {
                window.Dispatcher.BeginInvoke((Action)CloseWindow);
                return;
            }

            RoutedEventHandler? loadedHandler = null;
            loadedHandler = (_, _) =>
            {
                window.Loaded -= loadedHandler;
                window.Dispatcher.BeginInvoke((Action)CloseWindow);
            };

            window.Loaded += loadedHandler;
        }
    }
}
