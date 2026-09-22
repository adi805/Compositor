using Compositor.Core.Selection;

namespace Compositor.Core.Tools;

public enum GradientShape { Linear, Radial }

public enum GradientStyle { ForegroundToBackground, ForegroundToTransparent }

/// <summary>
/// Gradient options, upstream GradientSettings: linear runs start→end, radial
/// is centered on the start with the end on its rim; style picks the second
/// stop; opacity caps the applied alpha.
/// </summary>
public sealed record GradientSettings
{
    public GradientShape Shape { get; init; } = GradientShape.Linear;
    public GradientStyle Style { get; init; } = GradientStyle.ForegroundToTransparent;
    public bool Reversed { get; init; }
    public float Opacity { get; init; } = 1;
}

/// <summary>
/// Gradient fill, port of upstream Gradient.swift's fillGradient: two-stop
/// interpolation (foreground→background at full alpha, or foreground→
/// foreground fading to alpha 0), reversed swaps the stops, applied alpha is
/// stop-alpha × opacity, composed source-over from the pre-stroke pixels.
/// </summary>
public static class GradientFill
{
    public static void Apply(
        RasterSurface surface,
        float startX, float startY, float endX, float endY,
        byte fr, byte fg, byte fb,
        byte br, byte bg, byte bb,
        GradientSettings settings,
        Selection.SelectionClip? clip = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(settings);
        var dx = endX - startX;
        var dy = endY - startY;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared < 0.25f)
        {
            return; // upstream: no line, nothing pending
        }

        var opacity = Math.Clamp(settings.Opacity, 0f, 1f);
        var before = (byte[])surface.Pixels.Clone();
        var (c0r, c0g, c0b, c0a, c1r, c1g, c1b, c1a) = Stops(settings, fr, fg, fb, br, bg, bb);

        for (var y = 0; y < surface.Height; y++)
        {
            for (var x = 0; x < surface.Width; x++)
            {
                float t;
                if (settings.Shape == GradientShape.Radial)
                {
                    var ex = (x + 0.5f) - startX;
                    var ey = (y + 0.5f) - startY;
                    t = MathF.Sqrt((ex * ex) + (ey * ey)) / MathF.Sqrt(lengthSquared);
                }
                else
                {
                    // Linear: projection of the pixel onto the start→end axis.
                    t = (((x + 0.5f) - startX) * dx + ((y + 0.5f) - startY) * dy) / lengthSquared;
                }
                t = Math.Clamp(t, 0f, 1f);
                if (settings.Reversed)
                {
                    t = 1f - t;
                }

                var alpha = Math.Clamp((c0a + ((c1a - c0a) * t)) * opacity, 0f, 1f);
                if (alpha <= 0f)
                {
                    continue;
                }
                if (clip is { } clipMask)
                {
                    var factor = clipMask.FactorAt(x, y);
                    if (factor <= 0f)
                    {
                        continue;
                    }
                    alpha *= factor;
                }

                var r = (byte)Math.Round((c0r + ((c1r - c0r) * t)), MidpointRounding.AwayFromZero);
                var g = (byte)Math.Round((c0g + ((c1g - c0g) * t)), MidpointRounding.AwayFromZero);
                var b = (byte)Math.Round((c0b + ((c1b - c0b) * t)), MidpointRounding.AwayFromZero);

                var i = ((y * surface.Width) + x) * 4;
                var srcA = alpha;
                var dstA = before[i + 3] / 255f;
                var outA = srcA + (dstA * (1f - srcA));
                if (outA <= 0f)
                {
                    continue;
                }
                surface.Pixels[i] = ToByte(((r / 255f * srcA) + (before[i] / 255f * dstA * (1f - srcA))) / outA * 255f);
                surface.Pixels[i + 1] = ToByte(((g / 255f * srcA) + (before[i + 1] / 255f * dstA * (1f - srcA))) / outA * 255f);
                surface.Pixels[i + 2] = ToByte(((b / 255f * srcA) + (before[i + 2] / 255f * dstA * (1f - srcA))) / outA * 255f);
                surface.Pixels[i + 3] = ToByte(outA * 255f);
            }
        }
        surface.MarkDirty();
    }

    private static (float R, float G, float B, float A, float R2, float G2, float B2, float A2) Stops(
        GradientSettings settings, byte fr, byte fg, byte fb, byte br, byte bg, byte bb)
    {
        // Colors in 0..255 (straight), alphas in 0..1. Foreground-to-transparent
        // keeps the foreground hue at both stops.
        return settings.Style == GradientStyle.ForegroundToBackground
            ? (fr, fg, fb, 1f, br, bg, bb, 1f)
            : (fr, fg, fb, 1f, fr, fg, fb, 0f);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v + 0.5f, 0f, 255f);
}
