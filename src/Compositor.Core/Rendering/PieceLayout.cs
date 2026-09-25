namespace Compositor.Core.Rendering;

/// <summary>
/// Where one piece's pixels come from and how big its reduced image will be. The geometry half of
/// <c>TiledLayerRenderer.piece(interior:level:origin:bounds:compose:)</c>: composing and halving need a real
/// bitmap, and Core is not allowed to know about one, so this returns the plan and the App renderer carries
/// it out. Splitting it here is what lets the locality of a stroke be tested without a window.
/// </summary>
/// <param name="Interior">The square this piece is drawn inside, clipped to the bounds. Nothing outside it
/// is ever painted, which is why pieces can replace the whole-image draw instead of adding to it.</param>
/// <param name="Region">Grid pixels its image actually holds: the interior plus the margin of surrounding
/// pixels the halvings and the final resample can reach.</param>
/// <param name="Level">How many exact halvings separate <paramref name="region"/> from what gets drawn.</param>
public readonly record struct PieceLayout(PixelRect Interior, PixelRect Region, int Level)
{
    public int RegionWidth => (int)Region.Width;

    public int RegionHeight => (int)Region.Height;

    public long RegionPixels => (long)RegionWidth * RegionHeight;

    /// <summary>Size of the image after the halvings, which is what actually gets uploaded and drawn.</summary>
    public (int Width, int Height) ReducedSize => DownsampleLevels.AtLevel(RegionWidth, RegionHeight, Level);

    /// <summary>Where the composed region starts, in the layer's own coordinates.</summary>
    public double OffsetX => Region.MinX;

    public double OffsetY => Region.MinY;

    /// <summary>
    /// Plans the piece for one interior, with upstream's guards in upstream's order: align the grown interior
    /// to the halving grid, cut it down to what holds pixels, then refuse anything that would allocate past
    /// the cap. <paramref name="bounds"/> is "everything that holds pixels", so the margin is kept inside it:
    /// resampling a piece whose margin is empty pulls that emptiness into the layer's edge, which drawing the
    /// whole image never does.
    /// </summary>
    public static PieceLayout? Plan(
        PixelRect interior,
        int level,
        PixelRect? bounds = null,
        double originX = 0,
        double originY = 0)
    {
        if (level < 0 || level > DownsampleLevels.MaxLevel)
        {
            return null;
        }

        var step = 1L << level;
        var margin = TileGrid.Support(level);
        var region = TileGrid.Aligned(interior.Grow(margin), step, originX, originY);
        var kept = interior;

        if (bounds is { } rawBounds)
        {
            var limit = TileGrid.Aligned(rawBounds, step, originX, originY);
            region = region.Intersect(limit);
            if (region.IsEmpty)
            {
                return null;
            }

            kept = interior.Intersect(region);
            if (kept.IsEmpty)
            {
                return null;
            }
        }

        var width = (int)region.Width;
        var height = (int)region.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        if ((long)width * height > TileGrid.MaxPiecePixels)
        {
            return null;
        }

        return new PieceLayout(kept, region, level);
    }

    /// <summary>
    /// Every piece a set of changed rectangles has to rebuild at one level: the grid squares the changes
    /// reach (see <see cref="TileGrid.Interiors"/>), each planned as a piece. This is the whole locality
    /// story in one call, and its count is the number a stroke on a large layer is judged by: it has to
    /// grow with the stroke, not with the canvas.
    /// </summary>
    /// <param name="changed">Rectangles that changed, in grid pixels, usually
    /// <see cref="StrokeTiles.PatchRects"/>.</param>
    /// <param name="level">Resolution being drawn.</param>
    /// <param name="cell">Piece square edge: <see cref="TileGrid.StrokeCell"/> for a live stroke,
    /// <see cref="TileGrid.CommittedCell"/> for a committed snapshot.</param>
    /// <param name="bounds">Everything that holds pixels, or null to let pieces keep their margin.</param>
    /// <param name="visible">What the viewport shows; pieces outside it are not worth building.</param>
    public static IReadOnlyList<PieceLayout> Plans(
        IReadOnlyList<PixelRect> changed,
        int level,
        double cell = TileGrid.StrokeCell,
        PixelRect? bounds = null,
        PixelRect? visible = null,
        double originX = 0,
        double originY = 0)
    {
        var step = 1L << level;
        var interiors = TileGrid.Interiors(
            changed, TileGrid.Support(level), cell, step, originX, originY, visible);

        var pieces = new List<PieceLayout>(interiors.Count);
        foreach (var interior in interiors)
        {
            // A square the guards reject is skipped, not fatal: upstream's compactMap drops nil pieces and
            // draws the rest, and losing one square is a seam, while throwing loses the whole frame.
            if (Plan(interior, level, bounds, originX, originY) is { } piece)
            {
                pieces.Add(piece);
            }
        }

        return pieces;
    }
}
