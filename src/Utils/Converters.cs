using IcoConverter.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace IcoConverter.Utils
{
    public class MaskShapeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MaskShape shape)
            {
                return shape switch
                {
                    MaskShape.RoundedRectangle => "圆角矩形",
                    MaskShape.Circle => "圆形",
                    MaskShape.Ellipse => "椭圆",
                    MaskShape.Polygon => "正多边形",
                    _ => shape.ToString() ?? string.Empty
                };
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 仅单向转换，不支持 ConvertBack
            throw new NotSupportedException();
        }
    }


    /// <summary>
    /// 整数范围校验规则。
    /// </summary>
    public class IntRangeValidationRule : ValidationRule
    {
        public int Minimum { get; set; } = 0;
        public int Maximum { get; set; } = int.MaxValue;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var text = value?.ToString() ?? string.Empty;
            if (!int.TryParse(text, out var number))
            {
                return new ValidationResult(false, "请输入整数");
            }

            if (number < Minimum || number > Maximum)
            {
                return new ValidationResult(false, $"范围 {Minimum}-{Maximum}");
            }

            return ValidationResult.ValidResult;
        }
    }

    /// <summary>
    /// 浮点范围校验规则。
    /// </summary>
    public class DoubleRangeValidationRule : ValidationRule
    {
        public double Minimum { get; set; } = double.MinValue;
        public double Maximum { get; set; } = double.MaxValue;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var text = value?.ToString() ?? string.Empty;
            if (!double.TryParse(text, NumberStyles.Float, cultureInfo, out var number))
            {
                return new ValidationResult(false, "请输入数字");
            }

            if (number < Minimum || number > Maximum)
            {
                return new ValidationResult(false, $"范围 {Minimum}-{Maximum}");
            }

            return ValidationResult.ValidResult;
        }
    }
}
