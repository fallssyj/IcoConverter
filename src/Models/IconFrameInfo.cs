namespace IcoConverter.Models
{
    public readonly struct IconFrameInfo
    {
        public IconFrameInfo(int width, int height, int bitCount)
        {
            Width = width;
            Height = height;
            BitCount = bitCount;
        }

        public int Width { get; }

        public int Height { get; }

        public int BitCount { get; }

        public string DisplayText => $"{Width}×{Height} · {BitCount}bpp";
    }
}
