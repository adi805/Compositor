using Compositor.Core.Selection;

namespace Compositor.Core;

/// <summary>
/// Brush stroke entry points. The canonical path runs through
/// <see cref="StrokeCoverage"/> (upstream BrushStroke.swift): evenly spaced
/// tip stamps accumulate into a coverage mask (screen for soft, max for hard)
/// and paint through the stroke color at the stroke's opacity cap, so
/// overlapping dabs never darken beyond it.
/// </summary>
public static class BrushStroke
{
    /// <summary>
    /// Applies a stroke; mutates the surface in place. An optional selection
    /// clip constrains the stroke (upstream: brush edits respect the active
    /// selection, soft edges blend partially). Deterministic: coverage
    /// accumulation is order-independent, so live feedback, deferred redo and
    /// this path all produce identical pixels.
    /// </summary>
    public static void Apply(
        RasterSurface surface,
        IReadOnlyList<(float X, float Y)> path,
        BrushSettings settings,
        SelectionClip? selection = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            return;
        }

        var before = (byte[])surface.Pixels.Clone();
        var stroke = new StrokeCoverage(surface.Width, surface.Height, settings, selection);
        foreach (var point in path)
        {
            stroke.WalkTo(point.X, point.Y);
        }
        stroke.PaintRegion(surface, before);
    }

    /// <summary>Hard-round-brush adapter over <see cref="Apply"/> for callers
    /// that still express the brush as radius + color + opacity.</summary>
    public static void Apply(
        RasterSurface surface,
        IReadOnlyList<(float X, float Y)> path,
        float radius,
        byte r, byte g, byte b, byte a,
        float opacity,
        SelectionClip? selection = null)
    {
        Apply(
            surface, path,
            new BrushSettings
            {
                Diameter = MathF.Max(1f, radius * 2f),
                Hardness = 1f,
                R = r,
                G = g,
                B = b,
                A = a,
                Opacity = opacity,
            },
            selection);
    }

    /// <summary>Axis-aligned bounds a stroke would touch, clamped to the surface.</summary>
    public static (int X, int Y, int Width, int Height) Bounds(
        IReadOnlyList<(float X, float Y)> path, float radius, int surfaceWidth, int surfaceHeight)
    {
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        foreach (var (x, y) in path)
        {
            minX = MathF.Min(minX, x - radius);
            minY = MathF.Min(minY, y - radius);
            maxX = MathF.Max(maxX, x + radius);
            maxY = MathF.Max(maxY, y + radius);
        }

        var x0 = Math.Max(0, (int)MathF.Floor(minX));
        var y0 = Math.Max(0, (int)MathF.Floor(minY));
        var x1 = Math.Min(surfaceWidth - 1, (int)MathF.Ceiling(maxX) - 1);
        var y1 = Math.Min(surfaceHeight - 1, (int)MathF.Ceiling(maxY) - 1);
        return x1 < x0 || y1 < y0 ? (0, 0, 0, 0) : (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }
}
