using System.Runtime.InteropServices;
using Compositor.Core;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.App.Rendering;

/// <summary>
/// Turns <see cref="PieceLayout"/> plans into pixels. This is the other half of
/// <c>TiledLayerRenderer.piece</c>: compose the region at full resolution, then reduce it with the same
/// exact halvings the whole image uses, so a piece's pixels are the whole image's pixels and swapping one
/// in is invisible.
/// </summary>
/// <remarks>
/// Pieces come out individually rather than merged into one big surface, exactly like upstream: merging
/// would allocate the whole reduced image and undo the point. The caller draws the base image, then each
/// piece clipped hard to its interior.
/// <para>
/// The counters exist because "we rebuild less" is a claim, and a claim that is not counted is a wish.
/// <see cref="PixelsResampled"/> is what the high-quality resampler actually processed.
/// </para>
/// </remarks>
public sealed class PieceComposer
{
    private readonly long _pixelBudget;

    public PieceComposer(long? pixelBudget = null) =>
        _pixelBudget = pixelBudget ?? TileGrid.MaxPiecePixels;

    /// <summary>How many pieces the last <see cref="Compose"/> built.</summary>
    public int PiecesBuilt { get; private set; }

    /// <summary>Full-resolution pixels the resampler processed across all pieces built so far.</summary>
    public long PixelsResampled { get; private set; }

    /// <summary>Whole-surface pixels one non-tiled rebuild of this source would have cost.</summary>
    public static long PixelsForWholeSurface(RasterSurface source) => (long)source.Width * source.Height;

    /// <summary>One realized piece: where it goes, and the reduced pixels that fill it.</summary>
    /// <param name="Layout">The plan this was built from.</param>
    /// <param name="Image">The region reduced to the piece's level; size is
    /// <see cref="PieceLayout.ReducedSize"/>.</param>
    public readonly record struct Realized(PieceLayout Layout, RasterSurface Image);

    /// <summary>
    /// Builds every piece of <paramref name="pieces"/> out of <paramref name="source"/>. A piece whose
    /// region costs more than the cap is skipped rather than thrown: one oversized square must not lose
    /// the frame, and the caller can see the difference in <see cref="PiecesBuilt"/>.
    /// </summary>
    public IReadOnlyList<Realized> Compose(RasterSurface source, IReadOnlyList<PieceLayout> pieces, int level)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pieces);

        PiecesBuilt = 0;
        var built = new List<Realized>(pieces.Count);
        foreach (var layout in pieces)
        {
            var region = layout.Region;
            var width = layout.RegionWidth;
            var height = layout.RegionHeight;
            if (width <= 0 || height <= 0 || (long)width * height > _pixelBudget)
            {
                continue;
            }

            var cropped = Crop(source, region);
            if (cropped is null)
            {
                continue;
            }

            var reduced = cropped;
            var halved = true;
            for (var step = 0; step < level; step++)
            {
                var next = Halve(reduced);
                if (next is null)
                {
                    halved = false;
                    break;
                }

                reduced = next;
            }

            if (!halved)
            {
                continue;
            }

            PixelsResampled += (long)width * height;
            built.Add(new Realized(layout, reduced));
        }

        PiecesBuilt = built.Count;
        return built;
    }

    /// <summary>
    /// Copies a sub-rectangle out of a surface, in the surface's own pixel coordinates. A region reaching
    /// past the surface is clipped to what exists; a region with no overlap yields null, so a piece that
    /// points at nothing is dropped instead of drawing garbage.
    /// </summary>
    private static RasterSurface? Crop(RasterSurface source, PixelRect region)
    {
        var left = (int)Math.Floor(region.MinX);
        var top = (int)Math.Floor(region.MinY);
        var right = (int)Math.Ceiling(region.MaxX);
        var bottom = (int)Math.Ceiling(region.MaxY);
        var clipped = new PixelRect(left, top, right - left, bottom - top)
            .Intersect(new PixelRect(0, 0, source.Width, source.Height));
        var width = (int)clipped.Width;
        var height = (int)clipped.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        var stride = source.Width * 4;
        for (var y = 0; y < height; y++)
        {
            var from = (((int)clipped.MinY + y) * stride) + ((int)clipped.MinX * 4);
            if (from < 0 || from + width * 4 > source.Pixels.Length)
            {
                continue;
            }

            Array.Copy(source.Pixels, from, pixels, y * width * 4, width * 4);
        }

        return new RasterSurface(width, height, pixels);
    }

    /// <summary>
    /// Exactly half the size, rounded up, resampled at high quality. Same conversion
    /// <see cref="DownsampleCache"/> uses, kept here so a piece and the whole image reduce identically.
    /// </summary>
    private static RasterSurface? Halve(RasterSurface source)
    {
        var (width, height) = DownsampleLevels.Halve(source.Width, source.Height);
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var from = new SKBitmap(info);
        Marshal.Copy(source.Pixels, 0, from.GetPixels(), source.Pixels.Length);

        using var to = from.Resize(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            SKFilterQuality.High);
        if (to is null)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        Marshal.Copy(to.GetPixels(), pixels, 0, pixels.Length);
        return new RasterSurface(width, height, pixels);
    }
}
