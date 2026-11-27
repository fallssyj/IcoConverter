using System;
using System.Windows;

namespace IcoConverter.Models
{
    // 使用图像像素坐标。Right 和 Bottom 是不包含在选区内的边界。
    public readonly record struct CropRegion(int X, int Y, int Width, int Height)
    {
        public bool IsValid => Width > 0 && Height > 0;

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public Rect ToRect() => new(X, Y, Width, Height);

        public Int32Rect ToInt32Rect() => new(X, Y, Width, Height);

        // 画布非空时，将边界取整并限制在画布内，至少保留 1×1 像素。
        public static CropRegion? FromRect(Rect rect, int boundsWidth, int boundsHeight)
        {
            if (boundsWidth <= 0 || boundsHeight <= 0)
            {
                return null;
            }

            var left = (int)Math.Round(rect.Left, MidpointRounding.AwayFromZero);
            var top = (int)Math.Round(rect.Top, MidpointRounding.AwayFromZero);
            var right = (int)Math.Round(rect.Right, MidpointRounding.AwayFromZero);
            var bottom = (int)Math.Round(rect.Bottom, MidpointRounding.AwayFromZero);

            left = Math.Clamp(left, 0, boundsWidth - 1);
            top = Math.Clamp(top, 0, boundsHeight - 1);
            right = Math.Clamp(right, left + 1, boundsWidth);
            bottom = Math.Clamp(bottom, top + 1, boundsHeight);

            var region = new CropRegion(left, top, right - left, bottom - top);
            return region.IsValid ? region : null;
        }
    }
}
