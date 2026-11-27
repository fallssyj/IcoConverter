using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace IcoConverter.Controls
{
    public sealed class IntegerInput : TextBox
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(IntegerInput),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (target, _) => ((IntegerInput)target).DisplayValue()));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(int), typeof(IntegerInput), new PropertyMetadata(int.MinValue));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(int), typeof(IntegerInput), new PropertyMetadata(int.MaxValue));

        public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
            nameof(SmallChange), typeof(int), typeof(IntegerInput), new PropertyMetadata(1));

        public IntegerInput()
        {
            // 派生控件通过 TextBox 的资源键跟随 Fluent 主题切换。
            SetResourceReference(StyleProperty, typeof(TextBox));
            DisplayValue();
        }

        public int Value
        {
            get => (int)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public int Minimum
        {
            get => (int)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public int Maximum
        {
            get => (int)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public int SmallChange
        {
            get => (int)GetValue(SmallChangeProperty);
            set => SetValue(SmallChangeProperty, value);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            Commit(ReadText());
            base.OnLostKeyboardFocus(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        Commit(ReadText());
                        e.Handled = true;
                        break;
                    case Key.Up:
                        Commit((long)ReadText() + SmallChange);
                        e.Handled = true;
                        break;
                    case Key.Down:
                        Commit((long)ReadText() - SmallChange);
                        e.Handled = true;
                        break;
                }
            }

            base.OnPreviewKeyDown(e);
        }

        private int ReadText() => int.TryParse(Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            ? value
            : Value;

        private void Commit(long value)
        {
            var bounded = (int)Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
            SetCurrentValue(ValueProperty, bounded);
            GetBindingExpression(ValueProperty)?.UpdateTarget();
            DisplayValue();
        }

        private void DisplayValue() => Text = Value.ToString(CultureInfo.CurrentCulture);
    }
}
