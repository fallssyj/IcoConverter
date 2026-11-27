using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace IcoConverter.Controls
{
    // Selection 使用图像像素坐标，控件内部转换为显示坐标
    public sealed class CropOverlay : FrameworkElement
    {
        private const double DefaultHandleSize = 11d;
        private const double MinDisplaySize = 18d;

        private static readonly Brush DimBrush = CreateFrozenBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        private static readonly Brush HandleFill = Brushes.White;
        private static readonly Pen HandleStroke = CreateFrozenPen(Color.FromArgb(0xD0, 0x10, 0x10, 0x10), 1d);
        private static readonly Pen BorderPen = CreateFrozenPen(Color.FromArgb(0xFF, 0x4A, 0x9E, 0xFF), 1.6d);
        private static readonly Pen ShadowPen = CreateFrozenPen(Color.FromArgb(0x66, 0x00, 0x00, 0x00), 3.2d);
        private static readonly Pen ThirdPen = CreateFrozenPen(Color.FromArgb(0x9A, 0xFF, 0xFF, 0xFF), 1d);
        private static readonly Pen ThirdShadowPen = CreateFrozenPen(Color.FromArgb(0x4D, 0x00, 0x00, 0x00), 1d);
        private static readonly Brush LabelBackground = CreateFrozenBrush(Color.FromArgb(0xD8, 0x10, 0x10, 0x10));
        private static readonly Brush LabelForeground = Brushes.White;

        private DragZone _zone;
        private bool _isDragging;
        private Point _dragStart;
        private Rect _dragOrigin;

        static CropOverlay()
        {
            FocusableProperty.OverrideMetadata(typeof(CropOverlay), new FrameworkPropertyMetadata(false));
        }

        public CropOverlay()
        {
            ClipToBounds = true;
        }

        public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
            nameof(Selection),
            typeof(Rect),
            typeof(CropOverlay),
            new FrameworkPropertyMetadata(
                Rect.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>图像像素坐标</summary>
        public Rect Selection
        {
            get => (Rect)GetValue(SelectionProperty);
            set => SetValue(SelectionProperty, value);
        }

        public static readonly DependencyProperty ImageWidthProperty = DependencyProperty.Register(
            nameof(ImageWidth),
            typeof(double),
            typeof(CropOverlay),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public double ImageWidth
        {
            get => (double)GetValue(ImageWidthProperty);
            set => SetValue(ImageWidthProperty, value);
        }

        public static readonly DependencyProperty ImageHeightProperty = DependencyProperty.Register(
            nameof(ImageHeight),
            typeof(double),
            typeof(CropOverlay),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public double ImageHeight
        {
            get => (double)GetValue(ImageHeightProperty);
            set => SetValue(ImageHeightProperty, value);
        }

        public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
            nameof(AspectRatio),
            typeof(double),
            typeof(CropOverlay),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>宽/高，0 表示不锁比例</summary>
        public double AspectRatio
        {
            get => (double)GetValue(AspectRatioProperty);
            set => SetValue(AspectRatioProperty, value);
        }

        public static readonly DependencyProperty HandleSizeProperty = DependencyProperty.Register(
            nameof(HandleSize),
            typeof(double),
            typeof(CropOverlay),
            new FrameworkPropertyMetadata(DefaultHandleSize, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>控制柄的尺寸，单位为 WPF 设备无关像素。</summary>
        public double HandleSize
        {
            get => (double)GetValue(HandleSizeProperty);
            set => SetValue(HandleSizeProperty, value);
        }

        public static readonly DependencyProperty ShowSizeLabelProperty = DependencyProperty.Register(
            nameof(ShowSizeLabel),
            typeof(bool),
            typeof(CropOverlay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowSizeLabel
        {
            get => (bool)GetValue(ShowSizeLabelProperty);
            set => SetValue(ShowSizeLabelProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            // 铺一层透明矩形，否则空白区域无法接收鼠标事件
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

            var (displayRect, scale) = GetDisplayMetrics();
            if (scale <= 0 || displayRect.IsEmpty)
            {
                return;
            }

            var selection = GetSafeSelection();
            var rect = ToDisplay(selection, scale, displayRect);

            DrawDim(dc, displayRect, rect);

            dc.DrawRectangle(null, ShadowPen, rect);
            dc.DrawRectangle(null, BorderPen, rect);
            DrawThirds(dc, rect);

            if (ShowSizeLabel)
            {
                DrawSizeLabel(dc, rect, selection);
            }

            DrawHandles(dc, rect);
        }

        private void DrawDim(DrawingContext dc, Rect display, Rect selection)
        {
            var topHeight = Math.Max(0, selection.Top - display.Top);
            if (topHeight > 0)
            {
                dc.DrawRectangle(DimBrush, null, new Rect(display.Left, display.Top, display.Width, topHeight));
            }

            var bottomTop = Math.Min(display.Bottom, selection.Bottom);
            var bottomHeight = Math.Max(0, display.Bottom - bottomTop);
            if (bottomHeight > 0)
            {
                dc.DrawRectangle(DimBrush, null, new Rect(display.Left, bottomTop, display.Width, bottomHeight));
            }

            var middleTop = Math.Max(display.Top, selection.Top);
            var middleBottom = Math.Min(display.Bottom, selection.Bottom);
            if (middleBottom > middleTop)
            {
                var leftWidth = Math.Max(0, selection.Left - display.Left);
                if (leftWidth > 0)
                {
                    dc.DrawRectangle(DimBrush, null, new Rect(display.Left, middleTop, leftWidth, middleBottom - middleTop));
                }

                var rightLeft = Math.Min(display.Right, selection.Right);
                var rightWidth = Math.Max(0, display.Right - rightLeft);
                if (rightWidth > 0)
                {
                    dc.DrawRectangle(DimBrush, null, new Rect(rightLeft, middleTop, rightWidth, middleBottom - middleTop));
                }
            }
        }

        // 三分线下的深色描边偏移 1 个设备无关像素，使其在浅色图像上仍可辨识。
        private static void DrawThirds(DrawingContext dc, Rect selection)
        {
            for (var i = 1; i <= 2; i++)
            {
                var x = selection.Left + selection.Width * i / 3d;
                dc.DrawLine(ThirdShadowPen, new Point(x + 1, selection.Top), new Point(x + 1, selection.Bottom));
                dc.DrawLine(ThirdPen, new Point(x, selection.Top), new Point(x, selection.Bottom));

                var y = selection.Top + selection.Height * i / 3d;
                dc.DrawLine(ThirdShadowPen, new Point(selection.Left, y + 1), new Point(selection.Right, y + 1));
                dc.DrawLine(ThirdPen, new Point(selection.Left, y), new Point(selection.Right, y));
            }
        }

        private void DrawSizeLabel(DrawingContext dc, Rect selection, Rect imageSelection)
        {
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)Math.Round(imageSelection.Width)} × {(int)Math.Round(imageSelection.Height)}");

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                12d,
                LabelForeground,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            var padding = 6d;
            var width = formatted.Width + padding * 2;
            var height = formatted.Height + padding;

            var left = selection.Left;
            var top = selection.Top - height - 6;
            if (top < 0)
            {
                top = selection.Top + 6;
            }

            if (left + width > ActualWidth)
            {
                left = Math.Max(0, ActualWidth - width);
            }

            dc.DrawRoundedRectangle(LabelBackground, null, new Rect(left, top, width, height), 4, 4);
            dc.DrawText(formatted, new Point(left + padding, top + padding / 2));
        }

        private void DrawHandles(DrawingContext dc, Rect selection)
        {
            var size = Math.Max(6d, HandleSize);
            var half = size / 2d;

            foreach (var center in GetHandleCenters(selection))
            {
                var rect = new Rect(center.X - half, center.Y - half, size, size);
                dc.DrawRectangle(HandleFill, HandleStroke, rect);
            }
        }

        private static Point[] GetHandleCenters(Rect r) =>
        [
            new(r.Left, r.Top),
            new(r.Left + r.Width / 2d, r.Top),
            new(r.Right, r.Top),
            new(r.Right, r.Top + r.Height / 2d),
            new(r.Right, r.Bottom),
            new(r.Left + r.Width / 2d, r.Bottom),
            new(r.Left, r.Bottom),
            new(r.Left, r.Top + r.Height / 2d)
        ];

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (ImageWidth <= 0 || ImageHeight <= 0)
            {
                return;
            }

            var zone = HitTestZone(e.GetPosition(this));
            if (zone == DragZone.None)
            {
                return;
            }

            _zone = zone;
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            _dragOrigin = GetSafeSelection();
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var (displayRect, scale) = GetDisplayMetrics();
            if (scale <= 0)
            {
                return;
            }

            var position = e.GetPosition(this);

            if (!_isDragging)
            {
                _zone = HitTestZone(position);
                Cursor = GetCursor(_zone);
                return;
            }

            var dx = (position.X - _dragStart.X) / scale;
            var dy = (position.Y - _dragStart.Y) / scale;
            var updated = ComputeSelection(_dragOrigin, dx, dy, _zone, scale);
            SetValue(SelectionProperty, updated);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);

            if (_isDragging)
            {
                _isDragging = false;
                _zone = DragZone.None;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_isDragging)
            {
                Cursor = Cursors.Arrow;
            }
        }

        private Rect ComputeSelection(Rect origin, double dx, double dy, DragZone zone, double scale)
        {
            var minSize = Math.Max(2d, MinDisplaySize / Math.Max(scale, 0.0001));

            var left = origin.Left;
            var top = origin.Top;
            var right = origin.Right;
            var bottom = origin.Bottom;

            if (zone == DragZone.Move)
            {
                var width = origin.Width;
                var height = origin.Height;
                var x = Math.Clamp(origin.Left + dx, 0, Math.Max(0, ImageWidth - width));
                var y = Math.Clamp(origin.Top + dy, 0, Math.Max(0, ImageHeight - height));
                return new Rect(x, y, width, height);
            }

            if (zone.HasFlag(DragZone.Left))
            {
                left += dx;
            }

            if (zone.HasFlag(DragZone.Right))
            {
                right += dx;
            }

            if (zone.HasFlag(DragZone.Top))
            {
                top += dy;
            }

            if (zone.HasFlag(DragZone.Bottom))
            {
                bottom += dy;
            }

            if (left > right)
            {
                (left, right) = (right, left);
            }

            if (top > bottom)
            {
                (top, bottom) = (bottom, top);
            }

            left = Math.Clamp(left, 0, ImageWidth);
            right = Math.Clamp(right, 0, ImageWidth);
            top = Math.Clamp(top, 0, ImageHeight);
            bottom = Math.Clamp(bottom, 0, ImageHeight);

            var rect = new Rect(left, top, Math.Max(minSize, right - left), Math.Max(minSize, bottom - top));
            rect = ApplyAspectRatio(rect, origin, zone);
            return ClampToBounds(rect, minSize);
        }

        private Rect ApplyAspectRatio(Rect rect, Rect origin, DragZone zone)
        {
            var ratio = AspectRatio;
            if (ratio <= 0 || zone == DragZone.Move)
            {
                return rect;
            }

            var left = rect.Left;
            var top = rect.Top;
            var width = rect.Width;
            var height = rect.Height;

            if (IsCorner(zone))
            {
                // 分别计算两个方向，取变化量较大者，保持对角点不动
                var targetHeight = width / ratio;
                var targetWidth = height * ratio;

                if (Math.Abs(targetHeight - height) <= Math.Abs(targetWidth - width))
                {
                    height = targetHeight;
                }
                else
                {
                    width = targetWidth;
                }

                if (zone.HasFlag(DragZone.Left))
                {
                    left = rect.Right - width;
                }

                if (zone.HasFlag(DragZone.Top))
                {
                    top = rect.Bottom - height;
                }

                return new Rect(left, top, width, height);
            }

            if (zone is DragZone.Left or DragZone.Right)
            {
                var newHeight = width / ratio;
                var centerY = rect.Top + rect.Height / 2d;
                return new Rect(left, centerY - newHeight / 2d, width, newHeight);
            }

            var newWidth = height * ratio;
            var centerX = rect.Left + rect.Width / 2d;
            return new Rect(centerX - newWidth / 2d, top, newWidth, height);
        }

        private Rect ClampToBounds(Rect rect, double minSize)
        {
            var width = Math.Clamp(rect.Width, minSize, Math.Max(minSize, ImageWidth));
            var height = Math.Clamp(rect.Height, minSize, Math.Max(minSize, ImageHeight));
            var x = Math.Clamp(rect.X, 0, Math.Max(0, ImageWidth - width));
            var y = Math.Clamp(rect.Y, 0, Math.Max(0, ImageHeight - height));
            return new Rect(x, y, width, height);
        }

        private Rect GetSafeSelection()
        {
            var selection = Selection;
            if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
            {
                return new Rect(0, 0, Math.Max(1, ImageWidth), Math.Max(1, ImageHeight));
            }

            return ClampToBounds(selection, 1d);
        }

        private (Rect DisplayRect, double Scale) GetDisplayMetrics()
        {
            if (ImageWidth <= 0 || ImageHeight <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
            {
                return (Rect.Empty, 0d);
            }

            var scale = Math.Min(ActualWidth / ImageWidth, ActualHeight / ImageHeight);
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                return (Rect.Empty, 0d);
            }

            var width = ImageWidth * scale;
            var height = ImageHeight * scale;
            var left = (ActualWidth - width) / 2d;
            var top = (ActualHeight - height) / 2d;
            return (new Rect(left, top, width, height), scale);
        }

        private static Rect ToDisplay(Rect selection, double scale, Rect display) =>
            new(
                display.Left + selection.Left * scale,
                display.Top + selection.Top * scale,
                selection.Width * scale,
                selection.Height * scale);

        private DragZone HitTestZone(Point point)
        {
            var (displayRect, scale) = GetDisplayMetrics();
            if (scale <= 0 || !displayRect.Contains(point))
            {
                return DragZone.None;
            }

            var selection = ToDisplay(GetSafeSelection(), scale, displayRect);
            var size = Math.Max(6d, HandleSize);
            var half = size / 2d + 2d;

            var left = Math.Abs(point.X - selection.Left) <= half;
            var right = Math.Abs(point.X - selection.Right) <= half;
            var top = Math.Abs(point.Y - selection.Top) <= half;
            var bottom = Math.Abs(point.Y - selection.Bottom) <= half;
            var middleX = Math.Abs(point.X - (selection.Left + selection.Width / 2d)) <= half;
            var middleY = Math.Abs(point.Y - (selection.Top + selection.Height / 2d)) <= half;
            var insideX = point.X > selection.Left - half && point.X < selection.Right + half;
            var insideY = point.Y > selection.Top - half && point.Y < selection.Bottom + half;

            if ((left || right) && (top || bottom))
            {
                return top
                    ? (left ? DragZone.TopLeft : DragZone.TopRight)
                    : (left ? DragZone.BottomLeft : DragZone.BottomRight);
            }

            if ((left || right) && (middleY || (insideY && !top && !bottom)))
            {
                return left ? DragZone.Left : DragZone.Right;
            }

            if ((top || bottom) && (middleX || (insideX && !left && !right)))
            {
                return top ? DragZone.Top : DragZone.Bottom;
            }

            if (selection.Contains(point))
            {
                return DragZone.Move;
            }

            return DragZone.None;
        }

        private static bool IsCorner(DragZone zone) =>
            zone is DragZone.TopLeft or DragZone.TopRight or DragZone.BottomLeft or DragZone.BottomRight;

        private static Cursor GetCursor(DragZone zone) => zone switch
        {
            DragZone.Move => Cursors.SizeAll,
            DragZone.Left or DragZone.Right => Cursors.SizeWE,
            DragZone.Top or DragZone.Bottom => Cursors.SizeNS,
            DragZone.TopLeft or DragZone.BottomRight => Cursors.SizeNWSE,
            DragZone.TopRight or DragZone.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.Arrow
        };

        private static Brush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen CreateFrozenPen(Color color, double thickness)
        {
            var pen = new Pen(CreateFrozenBrush(color), thickness);
            pen.Freeze();
            return pen;
        }

        [Flags]
        private enum DragZone
        {
            None = 0,
            Left = 1,
            Right = 2,
            Top = 4,
            Bottom = 8,
            TopLeft = Top | Left,
            TopRight = Top | Right,
            BottomLeft = Bottom | Left,
            BottomRight = Bottom | Right,
            Move = 16
        }
    }
}
