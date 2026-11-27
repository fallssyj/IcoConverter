using System.Windows;
using System.Windows.Input;

namespace IcoConverter.Utils
{
    public static class DragDropCommandBehavior
    {
        public static readonly DependencyProperty DragOverCommandProperty =
            DependencyProperty.RegisterAttached(
                "DragOverCommand",
                typeof(ICommand),
                typeof(DragDropCommandBehavior),
                new PropertyMetadata(null, OnAnyCommandChanged));

        public static readonly DependencyProperty DropCommandProperty =
            DependencyProperty.RegisterAttached(
                "DropCommand",
                typeof(ICommand),
                typeof(DragDropCommandBehavior),
                new PropertyMetadata(null, OnAnyCommandChanged));

        public static readonly DependencyProperty DragLeaveCommandProperty =
            DependencyProperty.RegisterAttached(
                "DragLeaveCommand",
                typeof(ICommand),
                typeof(DragDropCommandBehavior),
                new PropertyMetadata(null, OnAnyCommandChanged));

        private static readonly DependencyProperty IsHookedProperty =
            DependencyProperty.RegisterAttached(
                "IsHooked",
                typeof(bool),
                typeof(DragDropCommandBehavior),
                new PropertyMetadata(false));

        public static void SetDragOverCommand(DependencyObject element, ICommand value)
            => element.SetValue(DragOverCommandProperty, value);

        public static ICommand GetDragOverCommand(DependencyObject element)
            => (ICommand)element.GetValue(DragOverCommandProperty);

        public static void SetDropCommand(DependencyObject element, ICommand value)
            => element.SetValue(DropCommandProperty, value);

        public static ICommand GetDropCommand(DependencyObject element)
            => (ICommand)element.GetValue(DropCommandProperty);

        public static void SetDragLeaveCommand(DependencyObject element, ICommand value)
            => element.SetValue(DragLeaveCommandProperty, value);

        public static ICommand GetDragLeaveCommand(DependencyObject element)
            => (ICommand)element.GetValue(DragLeaveCommandProperty);

        private static void OnAnyCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
            {
                return;
            }

            var hooked = (bool)element.GetValue(IsHookedProperty);
            if (hooked)
            {
                return;
            }

            element.PreviewDragOver += Element_PreviewDragOver;
            element.PreviewDrop += Element_PreviewDrop;
            element.PreviewDragLeave += Element_PreviewDragLeave;
            element.SetValue(IsHookedProperty, true);
        }

        private static void Element_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is not DependencyObject d)
            {
                return;
            }

            var command = GetDragOverCommand(d);
            if (command != null && command.CanExecute(e))
            {
                command.Execute(e);
            }
        }

        private static void Element_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is not DependencyObject d)
            {
                return;
            }

            var command = GetDropCommand(d);
            if (command != null && command.CanExecute(e))
            {
                command.Execute(e);
            }
        }

        private static void Element_PreviewDragLeave(object sender, System.Windows.DragEventArgs e)
        {
            if (sender is not DependencyObject d)
            {
                return;
            }

            var command = GetDragLeaveCommand(d);
            if (command != null && command.CanExecute(e))
            {
                command.Execute(e);
            }
        }
    }
}
