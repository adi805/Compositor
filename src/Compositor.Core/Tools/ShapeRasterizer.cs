using Compositor.Core.Selection;

namespace Compositor.Core.Tools;

public enum ShapeKind { Rectangle, Ellipse }

/// <summary>
/// Shape tool rasterizer, upstream ShapeTool.swift: rectangle (with optional
/// corner radius, clamped to half the shorter side — a large radius makes a
/// pill; ellipses ignore it) or ellipse filled into the rect. Coverage is
/// 2x2-supersampled, matching the selection shapes' antialiasing.
/// </summary>
public static class ShapeRasterizer
{
    public static void Fill(
        RasterSurface surface,
        float x, float y, float width, float height,
        ShapeKind kind, float cornerRadius,
        byte r, byte g, byte b, float opacity,
        Selection.SelectionClip? clip = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var x0 = Math.Max(0, (int)MathF.Floor(x));
        var y0 = Math.Max(0, (int)MathF.Floor(y));
        var x1 = Math.Min(surface.Width, (int)MathF.Ceiling(x + width));
        var y1 = Math.Min(surface.Height, (int)MathF.Ceiling(y + height));
        var radius = kind == ShapeKind.Rectangle
            ? Math.Clamp(cornerRadius, 0f, MathF.Min(width, height) / 2f)
            : 0f;
        var opacityClamped = Math.Clamp(opacity, 0f, 1f);

        for (var py = y0; py < y1; py++)
        {
            for (var px = x0; px < x1; px++)
            {
                var coverage = 0f;
                for (var sy = 0; sy < 2; sy++)
                {
                    for (var sx = 0; sx < 2; sx++)
                    {
                        var sampleX = px + 0.25f + (sx * 0.5f);
                        var sampleY = py + 0.25f + (sy * 0.5f);
                        if (Inside(kind, sampleX, sampleY, x, y, width, height, radius))
                        {
                            coverage += 0.25f;
                        }
                    }
                }
                if (coverage <= 0f)
                {
                    continue;
                }
                if (clip is { } clipMask)
                {
                    var factor = clipMask.FactorAt(px, py);
                    if (factor <= 0f)
                    {
                        continue;
                    }
                    coverage *= factor;
                }

                var alpha = coverage * opacityClamped;
                var i = ((py * surface.Width) + px) * 4;
                var dstA = surface.Pixels[i + 3] / 255f;
                var outA = alpha + (dstA * (1f - alpha));
                if (outA <= 0f)
                {
                    continue;
                }
                surface.Pixels[i] = ToByte(((r / 255f * alpha) + (surface.Pixels[i] / 255f * dstA * (1f - alpha))) / outA * 255f);
                surface.Pixels[i + 1] = ToByte(((g / 255f * alpha) + (surface.Pixels[i + 1] / 255f * dstA * (1f - alpha))) / outA * 255f);
                surface.Pixels[i + 2] = ToByte(((b / 255f * alpha) + (surface.Pixels[i + 2] / 255f * dstA * (1f - alpha))) / outA * 255f);
                surface.Pixels[i + 3] = ToByte(outA * 255f);
            }
        }
        surface.MarkDirty();
    }

    private static bool Inside(
        ShapeKind kind, float px, float py,
        float x, float y, float width, float height, float radius)
    {
        if (kind == ShapeKind.Ellipse)
        {
            var nx = (px - x - (width / 2f)) / (width / 2f);
            var ny = (py - y - (height / 2f)) / (height / 2f);
            return (nx * nx) + (ny * ny) <= 1f;
        }

        if (px < x || px >= x + width || py < y || py >= y + height)
        {
            return false;
        }
        if (radius <= 0f)
        {
            return true;
        }
        // Rounded corners: outside the inner rect, within the corner circle.
        var innerX = x + radius;
        var innerY = y + radius;
        var innerW = width - (radius * 2f);
        var innerH = height - (radius * 2f);
        if (px >= innerX && px < innerX + innerW && py >= innerY && py < innerY + innerH)
        {
            return true;
        }
        var cornerCx = px < innerX ? innerX : Math.Min(innerX + innerW, x + width - radius);
        var cornerCy = py < innerY ? innerY : Math.Min(innerY + innerH, y + height - radius);
        var dx = px - cornerCx;
        var dy = py - cornerCy;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v + 0.5f, 0f, 255f);
}
