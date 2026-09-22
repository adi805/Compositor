using Compositor.Core.Selection;

namespace Compositor.Core.Tools;

/// <summary>
/// Clone Stamp, upstream CloneStamp.swift: a sample of the layer (taken when
/// the stroke starts) is copied under the brush with the whole-pixel source
/// offset; <paramref name="aligned"/> keeps the first stroke's offset between
/// strokes (the caller stores it), otherwise each stroke re-runs from the
/// brush-to-source vector. Sample pixels outside the buffer contribute nothing.
/// </summary>
public static class CloneStroke
{
    /// <summary>Whole-pixel offset a stroke at <paramref name="point"/> copies
    /// with: aligned strokes keep the stored one; otherwise brush→source.</summary>
    public static (int Dx, int Dy) OffsetFor(
        (float X, float Y) source, (float X, float Y) point, (int Dx, int Dy)? alignedOffset)
    {
        if (alignedOffset is { } aligned)
        {
            return aligned;
        }
        return ((int)MathF.Round(source.X - point.X), (int)MathF.Round(source.Y - point.Y));
    }

    public static void Apply(
        RasterSurface surface,
        byte[] sample,
        IReadOnlyList<(float X, float Y)> path,
        (int Dx, int Dy) offset,
        BrushSettings settings,
        Selection.SelectionClip? clip = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(path);
        if (sample.Length != surface.Pixels.Length)
        {
            throw new ArgumentException("Sample size mismatch.", nameof(sample));
        }
        if (path.Count == 0)
        {
            return;
        }

        // Shift the sample by the offset into a same-size buffer, then paint it
        // through the coverage mask (sample alpha 0 contributes nothing).
        // Stroke at P copies from P + offset (upstream: offset = source - brush).
        var shifted = new byte[surface.Pixels.Length];
        for (var y = 0; y < surface.Height; y++)
        {
            var sy = y + offset.Dy;
            if (sy < 0 || sy >= surface.Height)
            {
                continue;
            }
            for (var x = 0; x < surface.Width; x++)
            {
                var sx = x + offset.Dx;
                if (sx < 0 || sx >= surface.Width)
                {
                    continue;
                }
                Array.Copy(
                    sample, ((sy * surface.Width) + sx) * 4,
                    shifted, ((y * surface.Width) + x) * 4,
                    4);
            }
        }

        var before = (byte[])surface.Pixels.Clone();
        var coverage = new StrokeCoverage(surface.Width, surface.Height, settings, clip);
        foreach (var point in path)
        {
            coverage.WalkTo(point.X, point.Y);
        }
        coverage.PaintSampleRegion(surface, before, shifted);
    }
}
