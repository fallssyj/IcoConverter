using System.Windows.Media;

namespace IcoConverter.Models;

public readonly record struct ImageRenderOptions(
    MaskShape Shape,
    int Radius,
    ImageTransformOptions Transform,
    CircleFitMode CircleFitMode,
    int CirclePaddingPercent,
    bool EnableEdgeSupersampling,
    int CanvasExpandPixels,
    Color CanvasFillColor,
    bool FillCanvasOutsideMask)
{
    internal bool RequiresProcessing =>
        Radius > 0 || !Transform.IsIdentity || CanvasExpandPixels != 0 || CanvasFillColor.A > 0;
}
