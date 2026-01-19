using IcoConverter.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace IcoConverter.Utils
{
    public class CornerQualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CornerQuality quality)
            {
                return quality switch
                {
                    CornerQuality.Low => "低质量",
                    CornerQuality.Medium => "中等质量",
                    CornerQuality.High => "高质量",
                    _ => value.ToString() ?? string.Empty
                };
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 此转换器为单向（One-way），不支持反向转换
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
}
