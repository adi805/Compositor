namespace Compositor.Core.Imaging;

/// <summary>
/// Whole-surface raster operations used by layer commands:
/// mirroring (flip), content trimming (merge result), and baking a
/// blend+opacity stack into one buffer (merge down / merge group).
/// All inputs are straight-alpha RGBA, 4 bytes per pixel.
/// </summary>
public static class SurfaceOps
{
    /// <summary>A mirrored copy of the surface (pixels only; transform flags are tracked separately).</summary>
    public static RasterSurface FlipCopy(RasterSurface source, bool horizontally)
    {
        ArgumentNullException.ThrowIfNull(source);
        var src = source.Pixels;
        var dst = new byte[src.Length];
        var (w, h) = (source.Width, source.Height);
        for (var y = 0; y < h; y++)
        {
            var rowIn = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var sx = horizontally ? w - 1 - x : x;
                var sy = horizontally ? y : h - 1 - y;
                var i = rowIn + x * 4;
                var j = sy * w * 4 + sx * 4;
                dst[i] = src[j];
                dst[i + 1] = src[j + 1];
                dst[i + 2] = src[j + 2];
                dst[i + 3] = src[j + 3];
            }
        }
        return new RasterSurface(w, h, dst);
    }

    /// <summary>
    /// The bounding box of non-transparent pixels. Returns null for a fully
    /// transparent surface (upstream PixelFilter.trimmed has nothing to keep).
    /// </summary>
    public static (int X, int Y, int Width, int Height)? ContentBounds(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                if (rgba[row + (x * 4) + 3] == 0)
                {
                    continue;
                }
                if (x < minX) { minX = x; }
                if (x > maxX) { maxX = x; }
                if (y < minY) { minY = y; }
                if (y > maxY) { maxY = y; }
            }
        }
        return maxX < 0 ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Crops the surface to the given rectangle (clamped to the surface).</summary>
    public static RasterSurface Crop(RasterSurface source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Crop size must be positive.");
        }
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        width = Math.Min(width, source.Width - x);
        height = Math.Min(height, source.Height - y);
        var dst = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(
                source.Pixels, ((y + row) * source.Width * 4) + (x * 4),
                dst, row * width * 4,
                width * 4);
        }
        return new RasterSurface(width, height, dst);
    }

    /// <summary>
    /// Bakes the given layers (bottom to top, visibility already applied by the
    /// caller) into one full-canvas straight-alpha buffer. Each layer contributes
    /// with its own opacity and blend mode, exactly like Flatten.
    /// </summary>
    public static RasterSurface CompositeStack(IReadOnlyList<Layer> layers, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var output = new byte[width * height * 4];
        foreach (var layer in layers)
        {
            if (!layer.IsVisible || layer.Pixels is not { } src || src.Width != width || src.Height != height)
            {
                continue;
            }
            var opacity = Math.Clamp(layer.Opacity, 0.0, 1.0);
            if (opacity <= 0.0)
            {
                continue;
            }
            var px = src.Pixels;
            for (var i = 0; i < output.Length; i += 4)
            {
                if (px[i + 3] == 0)
                {
                    continue;
                }
                Blend.Compose(
                    layer.Blend, (float)opacity,
                    output[i], output[i + 1], output[i + 2], output[i + 3],
                    px[i], px[i + 1], px[i + 2], px[i + 3],
                    out var r, out var g, out var b, out var a);
                output[i] = r;
                output[i + 1] = g;
                output[i + 2] = b;
                output[i + 3] = a;
            }
        }
        return new RasterSurface(width, height, output);
    }
}
