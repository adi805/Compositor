using Compositor.Core.Selection;

namespace Compositor.Core.Adjustments;

/// <summary>
/// Applies the adjustment engines to layer pixels: whole-surface LUT runs (Levels,
/// Curves, Exposure), per-pixel color math (Hue/Saturation), Gradient Map, and
/// Invert — each optionally blended back through a selection the way upstream
/// does: coverage × adjusted + (1 − coverage) × original, so fully selected pixels
/// stay exact and soft edges blend. Our surfaces store straight-alpha RGBA, so
/// LUT values apply directly to R/G/B and alpha passes through.
/// </summary>
public static class AdjustmentRunner
{
    /// <summary>Applies a 3×256 table of 0..1 floats, interpolating between entries like upstream's kernel.</summary>
    public static RasterSurface ApplyTables(RasterSurface source, float[] tables, SelectionClip? selection)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.Length != 768)
        {
            throw new ArgumentException("Tables must hold 3×256 entries.", nameof(tables));
        }

        var result = new RasterSurface(source.Width, source.Height);
        var src = source.Pixels;
        var dst = result.Pixels;
        Array.Copy(src, dst, src.Length);
        var count = source.Width * source.Height;
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            if (src[o + 3] == 0)
            {
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                var x = src[o + c];
                var lo = x;
                var hi = x < 255 ? x + 1 : 255;
                var table = c * 256;
                var f = (tables[table + lo] + ((tables[table + hi] - tables[table + lo]) * (x - lo))) * 255f;
                dst[o + c] = (byte)Math.Clamp(MathF.Round(f, MidpointRounding.AwayFromZero), 0, 255);
            }
        }

        BlendThroughSelection(result, source, selection);
        return result;
    }

    /// <summary>Hue/Saturation: per-pixel HSL math with the precomputed per-degree response.</summary>
    public static RasterSurface ApplyHueSaturation(
        RasterSurface source, HueSaturationSettings settings, SelectionClip? selection)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsIdentity)
        {
            return CloneSurface(source);
        }

        var response = settings.HueResponse();
        var result = new RasterSurface(source.Width, source.Height);
        var src = source.Pixels;
        var dst = result.Pixels;
        var count = source.Width * source.Height;
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            if (src[o + 3] == 0)
            {
                continue;
            }

            var (r, g, b) = settings.AdjustPixel(src[o] / 255d, src[o + 1] / 255d, src[o + 2] / 255d, response);
            dst[o] = ToByte(r);
            dst[o + 1] = ToByte(g);
            dst[o + 2] = ToByte(b);
            dst[o + 3] = src[o + 3];
        }

        BlendThroughSelection(result, source, selection);
        return result;
    }

    /// <summary>Exposure via its sRGB LUT (one table for all channels).</summary>
    public static RasterSurface ApplyExposure(
        RasterSurface source, ExposureSettings settings, SelectionClip? selection)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new ArgumentException("Exposure settings are invalid.", nameof(settings));
        }

        var table = settings.BuildTable();
        var tables = new float[768];
        for (var c = 0; c < 3; c++)
        {
            Array.Copy(table, 0, tables, c * 256, 256);
        }

        return ApplyTables(source, tables, selection);
    }

    /// <summary>Gradient Map via the Rec.709 luma lookup used by upstream's kernel.</summary>
    public static RasterSurface ApplyGradientMap(
        RasterSurface source, GradientMapSettings settings, SelectionClip? selection)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new ArgumentException("Gradient map settings are invalid.", nameof(settings));
        }

        var table = settings.BuildTable();
        var result = new RasterSurface(source.Width, source.Height);
        var src = source.Pixels;
        var dst = result.Pixels;
        var count = source.Width * source.Height;
        for (var i = 0; i < count; i++)
        {
            var o = i * 4;
            if (src[o + 3] == 0)
            {
                continue;
            }

            var level = ((2126 * src[o]) + (7152 * src[o + 1]) + (722 * src[o + 2]) + 5000) / 10000;
            var t = Math.Min(255, level) * 3;
            dst[o] = table[t];
            dst[o + 1] = table[t + 1];
            dst[o + 2] = table[t + 2];
            dst[o + 3] = src[o + 3];
        }

        BlendThroughSelection(result, source, selection);
        return result;
    }

    /// <summary>Whole-image invert (alpha kept), optionally limited to a selection.</summary>
    public static RasterSurface ApplyInvert(RasterSurface source, SelectionClip? selection)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new RasterSurface(source.Width, source.Height);
        var src = source.Pixels;
        var dst = result.Pixels;
        for (var i = 0; i < src.Length; i += 4)
        {
            dst[i] = (byte)(255 - src[i]);
            dst[i + 1] = (byte)(255 - src[i + 1]);
            dst[i + 2] = (byte)(255 - src[i + 2]);
            dst[i + 3] = src[i + 3];
        }

        BlendThroughSelection(result, source, selection);
        return result;
    }

    /// <summary>coverage × adjusted + (1 − coverage) × original, with rounding. Shared with the filter runner.</summary>
    internal static void BlendThroughSelection(
        RasterSurface adjusted, RasterSurface original, SelectionClip? selection)
    {
        if (selection is null)
        {
            return;
        }

        var adj = adjusted.Pixels;
        var org = original.Pixels;
        for (var y = 0; y < adjusted.Height; y++)
        {
            for (var x = 0; x < adjusted.Width; x++)
            {
                var factor = selection.FactorAt(x, y);
                if (factor >= 1)
                {
                    continue; // fully selected: adjusted pixels are already exact
                }

                var o = ((y * adjusted.Width) + x) * 4;
                if (factor <= 0)
                {
                    adj[o] = org[o];
                    adj[o + 1] = org[o + 1];
                    adj[o + 2] = org[o + 2];
                    adj[o + 3] = org[o + 3];
                    continue;
                }

                for (var c = 0; c < 4; c++)
                {
                    var blended = (adj[o + c] * factor) + (org[o + c] * (1f - factor));
                    adj[o + c] = (byte)Math.Clamp(MathF.Round(blended, MidpointRounding.AwayFromZero), 0, 255);
                }
            }
        }
    }

    private static RasterSurface CloneSurface(RasterSurface source)
    {
        var result = new RasterSurface(source.Width, source.Height);
        Array.Copy(source.Pixels, result.Pixels, source.Pixels.Length);
        return result;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255, MidpointRounding.AwayFromZero), 0, 255);
}

/// <summary>
/// Undoable pixel adjustment: keeps the layer's surface alive and the full before/after
/// buffers. Adjustments never change layer size, so whole-buffer swap is exact.
/// </summary>
public sealed class AdjustmentCommand : IUndoCommand
{
    private readonly RasterSurface _surface;
    private readonly byte[] _before;
    private readonly byte[] _after;
    private readonly string _name;

    public AdjustmentCommand(RasterSurface surface, byte[] before, byte[] after, string name)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _before = before ?? throw new ArgumentNullException(nameof(before));
        _after = after ?? throw new ArgumentNullException(nameof(after));
        _name = string.IsNullOrWhiteSpace(name) ? "Adjust" : name;
        if (before.Length != after.Length || before.Length != surface.Pixels.Length)
        {
            throw new ArgumentException("Before/after buffers must match the surface size.");
        }
    }

    public string Name => _name;

    public void Redo()
    {
        Array.Copy(_after, _surface.Pixels, _after.Length);
        _surface.MarkDirty();
    }

    public void Undo()
    {
        Array.Copy(_before, _surface.Pixels, _before.Length);
        _surface.MarkDirty();
    }
}
