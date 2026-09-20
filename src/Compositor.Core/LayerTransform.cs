namespace Compositor.Core;

/// <summary>
/// Geometry of a layer inside the canvas, mirroring the Mac v1+ layer
/// transform block (origin, size, clockwise rotation, flips).
/// Width/Height of 0 means "cover the whole canvas" (fresh blank layers);
/// rendering resolves that at draw time.
/// </summary>
public readonly record struct LayerTransform(
    double OriginX,
    double OriginY,
    double Width,
    double Height,
    double RotationDegrees,
    bool FlipH,
    bool FlipV)
{
    public static LayerTransform ForCanvas(int canvasWidth, int canvasHeight) =>
        new(0, 0, canvasWidth, canvasHeight, 0, false, false);

    public bool CoversCanvas => Width <= 0 || Height <= 0;
}
