using IcoConverter.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace IcoConverter.Utils
{
    public class RadiusUnitConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RadiusUnit unit)
            {
                return unit switch
                {
                    RadiusUnit.Pixels => "像素 (px)",
                    RadiusUnit.Percent => "百分比 (%)",
                    _ => unit.ToString() ?? string.Empty
                };
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class CircleFitModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CircleFitMode mode)
            {
                return mode switch
                {
                    CircleFitMode.Inscribed => "内切（默认）",
                    CircleFitMode.Circumscribed => "外切（按长边，超出裁掉）",
                    CircleFitMode.Padded => "保留留白",
                    _ => mode.ToString() ?? string.Empty
                };
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class CanvasFillModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CanvasFillMode mode)
            {
                return mode switch
                {
                    CanvasFillMode.Transparent => "透明",
                    CanvasFillMode.White => "白色",
                    CanvasFillMode.Black => "黑色",
                    CanvasFillMode.Custom => "自定义",
                    _ => mode.ToString() ?? string.Empty
                };
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class SupersampleModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SupersampleMode mode)
            {
                return mode switch
                {
                    SupersampleMode.Off => "关闭（最快）",
                    SupersampleMode.Preview => "仅预览",
                    SupersampleMode.Export => "仅导出",
                    SupersampleMode.Both => "预览 + 导出（推荐）",
                    _ => mode.ToString() ?? string.Empty
                };
            }

            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class IcoPngFrameModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IcoPngFrameMode mode)
            {
                return mode switch
                {
                    IcoPngFrameMode.Auto => "自动（256 及以上用 PNG）",
                    IcoPngFrameMode.AlwaysPng => "全部 PNG",
                    IcoPngFrameMode.NeverPng => "全部 BMP（兼容老软件）",
                    _ => mode.ToString() ?? string.Empty
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
                // Invert 为 true 时取反，避免为「隐藏」状态单独定义属性
                if (string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
                {
                    boolValue = !boolValue;
                }

                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 只给单向绑定用
            throw new NotSupportedException();
        }
    }

    public class NotificationLevelToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NotificationLevel severity)
            {
                var key = severity switch
                {
                    NotificationLevel.Success => "IconSuccess",
                    NotificationLevel.Warning => "IconWarning",
                    NotificationLevel.Error => "IconError",
                    _ => "IconInfo"
                };

                return System.Windows.Application.Current?.TryFindResource(key) ?? System.Windows.Application.Current!.FindResource("IconInfo");
            }

            return System.Windows.Application.Current!.FindResource("IconInfo");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isNull = value == null;
            var invert = string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase);

            if (invert)
            {
                isNull = !isNull;
            }

            return isNull ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }


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
