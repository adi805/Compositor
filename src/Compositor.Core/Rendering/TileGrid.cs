namespace Compositor.Core.Rendering;

/// <summary>
/// Which grid squares a set of changed rectangles has to rebuild, and how far beyond a change its reduced,
/// resampled pixels can reach. Ported from <c>Rendering/TiledLayerRenderer.swift</c> (<c>support(level:)</c>,
/// <c>aligned(_:step:origin:)</c>, <c>interiors(near:margin:size:step:origin:visible:)</c>).
/// </summary>
/// <remarks>
/// Drawing each tile on its own resamples it without its neighbours (seams) and cannot use the sharp halvings.
/// So changed areas are rebuilt as whole squares of the layer grid, aligned to every halving, recomposed at
/// full resolution with a margin of surrounding pixels, reduced with the same halvings, and drawn only inside
/// the square. The margin covers everything the halvings and the final resample can reach, so a piece's pixels
/// match the whole image's. This is also what keeps a big canvas cheap: a stroke rebuilds the squares it
/// touches, not the surface.
/// </remarks>
public static class TileGrid
{
    /// <summary>
    /// Piece squares: committed snapshots use large ones (fewer to build, once), live strokes small ones
    /// (little to rebuild per mouse move). Both are whole multiples of every halving used.
    /// </summary>
    public const double CommittedCell = 1024;

    public const double StrokeCell = 256;

    /// <summary>Cap on one piece's pixels, matching upstream's guard before it allocates.</summary>
    public const long MaxPiecePixels = 64_000_000;

    /// <summary>
    /// Grid pixels beyond a change that its reduced, resampled pixels can reach, with room to spare:
    /// 8 at full resolution, then 16 &lt;&lt; level.
    /// </summary>
    public static double Support(int level) => level == 0 ? 8d : 16 << level;

    /// <summary>
    /// Snaps a rectangle outwards to the <paramref name="step"/> grid, measured from the layer origin. Used so
    /// a piece's region lines up with every halving.
    /// </summary>
    public static PixelRect Aligned(PixelRect rect, double step, double originX, double originY)
    {
        var minX = originX + Math.Floor((rect.MinX - originX) / step) * step;
        var minY = originY + Math.Floor((rect.MinY - originY) / step) * step;
        var maxX = originX + Math.Ceiling((rect.MaxX - originX) / step) * step;
        var maxY = originY + Math.Ceiling((rect.MaxY - originY) / step) * step;
        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// In each <paramref name="size"/> square (measured from the layer origin), the part within
    /// <paramref name="margin"/> of any of <paramref name="rects"/>, grown to the <paramref name="step"/> grid.
    /// Pieces stay disjoint and go only where changes can show, limited to what <paramref name="visible"/>
    /// shows. Returned sorted by row then column so a caller can reproduce an order.
    /// </summary>
    public static IReadOnlyList<PixelRect> Interiors(
        IReadOnlyList<PixelRect> rects,
        double margin,
        double size,
        double step,
        double originX,
        double originY,
        PixelRect? visible)
    {
        var parts = new Dictionary<(int X, int Y), PixelRect>();

        foreach (var rect in rects)
        {
            var grown = rect.Grow(margin);
            if (visible is { } shown && !grown.Intersects(shown)) continue;

            var x0 = (int)Math.Floor((grown.MinX - originX) / size);
            var x1 = (int)Math.Ceiling((grown.MaxX - originX) / size);
            var y0 = (int)Math.Floor((grown.MinY - originY) / size);
            var y1 = (int)Math.Ceiling((grown.MaxY - originY) / size);
            if (x1 <= x0 || y1 <= y0) continue;

            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var square = new PixelRect(originX + x * size, originY + y * size, size, size);
                    var part = grown.Intersect(square);
                    if (part.IsEmpty) continue;
                    var key = (x, y);
                    parts[key] = parts.TryGetValue(key, out var existing) ? existing.Union(part) : part;
                }
            }
        }

        var interiors = new List<PixelRect>(parts.Count);
        foreach (var part in parts.Values)
        {
            // Squares sit on the step grid, so growing a part to it never leaves its square.
            var interior = Aligned(part, step, originX, originY);
            if (visible is { } shown && !interior.Intersects(shown)) continue;
            interiors.Add(interior);
        }

        interiors.Sort(static (a, b) => a.MinY != b.MinY ? a.MinY.CompareTo(b.MinY) : a.MinX.CompareTo(b.MinX));
        return interiors;
    }
}
