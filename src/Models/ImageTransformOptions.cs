using System;

namespace IcoConverter.Models
{
    /// <summary>
    /// 控制源图像缩放与偏移的参数。
    /// </summary>
    public readonly record struct ImageTransformOptions(double Scale, double OffsetX, double OffsetY)
    {
        public static readonly ImageTransformOptions Identity = new(1d, 0d, 0d);

        public bool IsIdentity => Math.Abs(Scale - 1d) < 0.0001 && Math.Abs(OffsetX) < 0.01 && Math.Abs(OffsetY) < 0.01;
    }
}
