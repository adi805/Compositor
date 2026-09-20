namespace Compositor.Core;

/// <summary>
/// Placeholder raster surface. Phase 2 replaces this with a sparse-tile
/// implementation backed by SkiaSharp / native kernels.
/// For now it just tracks dimensions so the document model and I/O are
/// testable without a rendering engine.
/// </summary>
public sealed class RasterSurface
{
    public int Width { get; }
    public int Height { get; }

    public RasterSurface(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
    }
}
