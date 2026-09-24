namespace Compositor.Core;

/// <summary>What a drag on the transform box is doing.</summary>
public enum TransformDragMode
{
    Move,
    Resize,
    Rotate,
}

/// <summary>What a pointer position lands on: a handle to drag, or nothing.</summary>
public readonly record struct TransformHit(TransformDragMode Mode, int Index)
{
    public static readonly TransformHit None = new(TransformDragMode.Move, -1);
    public bool IsNone => Index < 0;
}

/// <summary>
/// The eight handles on a layer's box and where they sit, plus what a click lands on. Ported from
/// <c>Rendering/TransformOverlay.swift</c> (<c>TransformOverlayGeometry</c>) and
/// <c>Document/LayerTransform.swift</c> (<c>handles</c>).
/// </summary>
/// <remarks>
/// Handles are in the box's own unit square so the same eight indices work at any size and rotation:
/// 0 top-left, 1 top-middle, 2 top-right, 3 middle-right, 4 bottom-right, 5 bottom-middle,
/// 6 bottom-left, 7 middle-left. All distances here are in document pixels; the caller converts a
/// screen-space grab radius by dividing by the zoom.
/// </remarks>
public static class TransformHandles
{
    /// <summary>The eight unit positions, in index order.</summary>
    public static readonly (double X, double Y)[] Unit =
    [
        (0.0, 0.0), (0.5, 0.0), (1.0, 0.0),
        (1.0, 0.5), (1.0, 1.0), (0.5, 1.0),
        (0.0, 1.0), (0.0, 0.5),
    ];

    /// <summary>Screen-space grab radius upstream uses, before converting to document pixels.</summary>
    public const double ScreenGrabRadius = 10;

    /// <summary>How far the rotation handle sits from the top-middle handle, in screen pixels.</summary>
    public const double RotationHandleScreenOffset = 28;

    /// <summary>Where the eight handles are in document pixels, in index order.</summary>
    public static (double X, double Y)[] Points(LayerTransform transform)
    {
        var points = new (double X, double Y)[Unit.Length];
        for (var i = 0; i < Unit.Length; i++)
        {
            points[i] = transform.Point(Unit[i].X, Unit[i].Y);
        }

        return points;
    }

    /// <summary>
    /// Where the rotation handle sits: <paramref name="offset"/> document pixels above the top-middle
    /// handle, following the layer's rotation so it stays on the box's outward normal.
    /// </summary>
    public static (double X, double Y) RotationHandle(LayerTransform transform, double offset)
    {
        var top = transform.Point(Unit[1].X, Unit[1].Y);
        return (top.X + Math.Sin(transform.Radians) * offset,
                top.Y - Math.Cos(transform.Radians) * offset);
    }

    /// <summary>
    /// What <paramref name="documentX"/>/<paramref name="documentY"/> grabs, or <see cref="TransformHit.None"/>.
    /// A corner or edge midpoint within <paramref name="radius"/> wins, then anywhere along an edge between two
    /// corners, which is what makes a whole edge draggable rather than only its midpoint square. The rotation
    /// handle is checked first so it stays grabbable when it sits close to the top edge.
    /// </summary>
    public static TransformHit Hit(
        LayerTransform transform,
        double documentX,
        double documentY,
        double radius,
        double rotationOffset)
    {
        var points = Points(transform);
        var rotation = RotationHandle(transform, rotationOffset);
        if (Near(documentX, documentY, rotation.X, rotation.Y, radius))
        {
            return new TransformHit(TransformDragMode.Rotate, 8);
        }

        for (var i = 0; i < points.Length; i++)
        {
            if (Near(documentX, documentY, points[i].X, points[i].Y, radius))
            {
                return new TransformHit(TransformDragMode.Resize, i);
            }
        }

        // Whole edges, not just the midpoint squares: (0,2) midpoint 1, (2,4) midpoint 3, and so on.
        foreach (var (start, end, handle) in new[] { (0, 2, 1), (2, 4, 3), (4, 6, 5), (6, 0, 7) })
        {
            var a = points[start];
            var b = points[end];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 0) continue;
            var t = ((documentX - a.X) * dx + (documentY - a.Y) * dy) / lengthSquared;
            if (t is < 0 or > 1) continue;
            if (Near(documentX, documentY, a.X + t * dx, a.Y + t * dy, radius))
            {
                return new TransformHit(TransformDragMode.Resize, handle);
            }
        }

        return TransformHit.None;
    }

    private static bool Near(double x, double y, double otherX, double otherY, double radius)
        => Math.Sqrt((x - otherX) * (x - otherX) + (y - otherY) * (y - otherY)) <= radius;
}
