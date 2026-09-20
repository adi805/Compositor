namespace Compositor.Core;

/// <summary>
/// Raster surface: 8-bit straight-alpha RGBA pixels, row-major, top-left origin.
/// Straight (non-premultiplied) alpha; compositing accounts for it.
/// </summary>
public sealed class RasterSurface
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    /// <summary>
    /// Monotonic mutation counter. Editors use it to detect stale cached
    /// previews of this surface without diffing pixel buffers.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>Call after any out-of-band mutation of <see cref="Pixels"/>.</summary>
    public void MarkDirty() => Version++;

    public RasterSurface(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
    }

    public RasterSurface(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height * 4)
        {
            throw new ArgumentException(
                $"Pixel buffer is {pixels.Length} bytes; expected {width * height * 4}.");
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>Sets a pixel from straight-alpha RGBA components.</summary>
    public void SetPixel(int x, int y, byte r, byte g, byte b, byte a)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        var i = ((y * Width) + x) * 4;
        Pixels[i] = r;
        Pixels[i + 1] = g;
        Pixels[i + 2] = b;
        Pixels[i + 3] = a;
    }

    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return (0, 0, 0, 0);
        }

        var i = ((y * Width) + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void Clear() => Array.Clear(Pixels);
}
