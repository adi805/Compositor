namespace Compositor.Core;

/// <summary>
/// Round soft brush: stamps a filled disc with linear edge falloff along the
/// pointer path, composited source-over into a straight-alpha surface.
/// Stroke opacity caps coverage; repeated stamps along overlapping segments
/// saturate the same way a physical stroke does.
/// </summary>
public static class BrushStroke
{
    /// <summary>Applies a stroke; mutates the surface in place.</summary>
    public static void Apply(
        RasterSurface surface,
        IReadOnlyList<(float X, float Y)> path,
        float radius,
        byte r, byte g, byte b, byte a,
        float opacity)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0 || radius <= 0)
        {
            return;
        }

        opacity = Math.Clamp(opacity, 0f, 1f);
        var spacing = MathF.Max(1f, radius * 0.5f);
        surface.MarkDirty();

        for (var i = 0; i < path.Count; i++)
        {
            if (i == 0)
            {
                Stamp(surface, path[0].X, path[0].Y, radius, r, g, b, a, opacity);
                continue;
            }

            var (x0, y0) = path[i - 1];
            var (x1, y1) = path[i];
            var dx = x1 - x0;
            var dy = y1 - y0;
            var length = MathF.Sqrt((dx * dx) + (dy * dy));
            var steps = Math.Max(1, (int)MathF.Ceiling(length / spacing));
            for (var s = 1; s <= steps; s++)
            {
                var t = s / (float)steps;
                Stamp(surface, x0 + (dx * t), y0 + (dy * t), radius, r, g, b, a, opacity);
            }
        }
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

    private static void Stamp(
        RasterSurface surface, float cx, float cy,
        float radius, byte r, byte g, byte b, byte a, float opacity)
    {
        var x0 = Math.Max(0, (int)MathF.Floor(cx - radius));
        var y0 = Math.Max(0, (int)MathF.Floor(cy - radius));
        var x1 = Math.Min(surface.Width - 1, (int)MathF.Ceiling(cx + radius));
        var y1 = Math.Min(surface.Height - 1, (int)MathF.Ceiling(cy + radius));

        var srcA = (a / 255f) * opacity;
        if (srcA <= 0f)
        {
            return;
        }

        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var dx = (x + 0.5f) - cx;
                var dy = (y + 0.5f) - cy;
                var dist = MathF.Sqrt((dx * dx) + (dy * dy));
                if (dist > radius)
                {
                    continue;
                }

                var falloff = 1f - (dist / radius);
                var stampAlpha = srcA * falloff;
                BlendPixel(surface, x, y, r, g, b, stampAlpha);
            }
        }
    }

    private static void BlendPixel(RasterSurface surface, int x, int y, byte r, byte g, byte b, float alpha)
    {
        var i = ((y * surface.Width) + x) * 4;
        var dstR = surface.Pixels[i] / 255f;
        var dstG = surface.Pixels[i + 1] / 255f;
        var dstB = surface.Pixels[i + 2] / 255f;
        var dstA = surface.Pixels[i + 3] / 255f;

        var outA = alpha + (dstA * (1f - alpha));
        if (outA <= 0f)
        {
            return;
        }

        surface.Pixels[i] = ToByte(((r / 255f * alpha) + (dstR * dstA * (1f - alpha))) / outA);
        surface.Pixels[i + 1] = ToByte(((g / 255f * alpha) + (dstG * dstA * (1f - alpha))) / outA);
        surface.Pixels[i + 2] = ToByte(((b / 255f * alpha) + (dstB * dstA * (1f - alpha))) / outA);
        surface.Pixels[i + 3] = ToByte(outA);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f + 0.5f, 0f, 255f);
}
