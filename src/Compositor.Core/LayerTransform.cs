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

    /// <summary>Center of the unrotated bounds in document pixels (upstream LayerTransform.center).</summary>
    public double CenterX => OriginX + Width / 2;
    public double CenterY => OriginY + Height / 2;

    /// <summary>
    /// Finite geometry within upstream's bounds: sizes 1..300000, origins within
    /// ±1e6. Width/Height of 0 (cover-canvas blank layers) also count as valid.
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (!IsFinite(OriginX) || !IsFinite(OriginY) || !IsFinite(Width)
                || !IsFinite(Height) || !IsFinite(RotationDegrees))
            {
                return false;
            }
            var sized = CoversCanvas || (Width is >= 1 and <= 300_000 && Height is >= 1 and <= 300_000);
            return sized && Math.Abs(OriginX) <= 1_000_000 && Math.Abs(OriginY) <= 1_000_000;
        }
    }

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

    /// <summary>
    /// Both sides set to <paramref name="percent"/> of the layer's pixel size,
    /// keeping the center, rotation and flips (upstream scaled(toPercent:)).
    /// </summary>
    public LayerTransform Scaled(double percent, double pixelWidth, double pixelHeight)
    {
        var w = Math.Max(1, pixelWidth * percent / 100.0);
        var h = Math.Max(1, pixelHeight * percent / 100.0);
        return new LayerTransform(CenterX - w / 2, CenterY - h / 2, w, h, RotationDegrees, FlipH, FlipV);
    }

    /// <summary>Whole pixels and whole degrees: what dragging and scaling leave behind (upstream rounded()).</summary>
    public LayerTransform Rounded() =>
        new(
            Math.Round(OriginX, MidpointRounding.AwayFromZero),
            Math.Round(OriginY, MidpointRounding.AwayFromZero),
            Math.Max(1, Math.Round(Width, MidpointRounding.AwayFromZero)),
            Math.Max(1, Math.Round(Height, MidpointRounding.AwayFromZero)),
            Math.Round(RotationDegrees, MidpointRounding.AwayFromZero),
            FlipH,
            FlipV);

    /// <summary>
    /// This placement mirrored across a vertical (or horizontal) line: the picture
    /// flips, its angle turns the other way, and its middle crosses to the other
    /// side of the line (upstream mirrored(horizontally:across:)).
    /// </summary>
    public LayerTransform Mirrored(bool horizontally, double axis) =>
        horizontally
            ? new LayerTransform(2 * axis - CenterX - Width / 2, OriginY, Width, Height, -RotationDegrees, !FlipH, FlipV)
            : new LayerTransform(OriginX, 2 * axis - CenterY - Height / 2, Width, Height, -RotationDegrees, FlipH, !FlipV);

    /// <summary>Rotation in radians.</summary>
    public double Radians => RotationDegrees * Math.PI / 180.0;

    /// <summary>
    /// Whether a document point is inside the box, rotation included: the point is taken into the box's own
    /// frame and compared against the half extents (upstream LayerTransform.contains(_:)).
    /// </summary>
    public bool Contains(double x, double y)
    {
        var dx = x - CenterX;
        var dy = y - CenterY;
        var cos = Math.Cos(Radians);
        var sin = Math.Sin(Radians);
        return Math.Abs(dx * cos + dy * sin) <= Width / 2
            && Math.Abs(-dx * sin + dy * cos) <= Height / 2;
    }

    /// <summary>
    /// Where a point of the box's own unit square lands in document pixels: (0,0) is the
    /// top-left corner, (1,1) the bottom-right, (0.5,0.5) the centre. The unit square is
    /// measured from the centre and then spun by the rotation, so a rotated layer's
    /// corners come out where they are drawn (upstream LayerTransform.point(_:)).
    /// </summary>
    public (double X, double Y) Point(double unitX, double unitY)
    {
        var x = (unitX - 0.5) * Width;
        var y = (unitY - 0.5) * Height;
        var cos = Math.Cos(Radians);
        var sin = Math.Sin(Radians);
        return (CenterX + x * cos - y * sin, CenterY + x * sin + y * cos);
    }
}
