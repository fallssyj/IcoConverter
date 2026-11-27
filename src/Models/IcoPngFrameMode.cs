namespace IcoConverter.Models
{
    /// <summary>
    /// ICO 帧的编码方式。Auto 对长边达到 256 像素的帧使用 PNG。
    /// NeverPng 为无法读取 PNG 帧的旧版软件强制输出 DIB。
    /// </summary>
    public enum IcoPngFrameMode
    {
        Auto,
        AlwaysPng,
        NeverPng
    }
}
