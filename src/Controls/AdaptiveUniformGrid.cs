using System;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace IcoConverter.Controls
{
    /// <summary>
    /// 按可用宽度自动计算列数的等宽网格。
    /// 可用宽度由各列均分，最后一行从左侧开始排列。
    /// </summary>
    public sealed class AdaptiveUniformGrid : UniformGrid
    {
        /// <summary>计算列数时使用的目标最小列宽。可用宽度更窄时保留一列。</summary>
        public static readonly DependencyProperty MinColumnWidthProperty =
            DependencyProperty.Register(
                nameof(MinColumnWidth),
                typeof(double),
                typeof(AdaptiveUniformGrid),
                new FrameworkPropertyMetadata(108d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinColumnWidth
        {
            get => (double)GetValue(MinColumnWidthProperty);
            set => SetValue(MinColumnWidthProperty, value);
        }

        protected override Size MeasureOverride(Size constraint)
        {
            // 宽度受限时才能均分；配合 HorizontalScrollBarVisibility=Disabled 使用
            if (!double.IsInfinity(constraint.Width) && constraint.Width > 0 && MinColumnWidth > 0)
            {
                var columns = Math.Max(1, (int)Math.Floor(constraint.Width / MinColumnWidth));
                if (columns != Columns)
                {
                    // 列数相同时避免重复写入，以免再次使测量失效。
                    Columns = columns;
                }
            }

            return base.MeasureOverride(constraint);
        }
    }
}
