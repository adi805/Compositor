namespace Compositor.Core.Rendering;

/// <summary>
/// The tiles a stroke has actually touched, and nothing else. Ported from <c>Document/BrushStroke.swift</c>
/// (<c>tileSize</c>, <c>tiles: [Int: Tile]</c>, <c>dirtyTiles: [Int: CGRect]</c>).
/// </summary>
/// <remarks>
/// This is the half that makes a big canvas cheap, and it is why the tiling was ported at all. A brush that
/// paints into one full-size surface allocates and rebuilds the whole surface no matter where the stroke
/// went; upstream allocates a context per 256-pixel tile lazily, so a dab in the corner of a 4096x4096
/// layer touches one tile. The tile edge is deliberately the same constant the renderer's live-stroke grid
/// uses (<see cref="TileGrid.StrokeCell"/>), because upstream measures wider tiles as no faster for wide
/// brushes and slower for narrow ones.
/// <para>
/// Dirty rects are tracked in TILE-LOCAL pixels, like upstream: the tile is the unit that gets republished,
/// and a publisher needs to know which part of it changed since the last publish, not where it is on the
/// layer.
/// </para>
/// </remarks>
public sealed class StrokeTiles
{
    private readonly Dictionary<int, PixelRect> _tiles = [];
    private readonly Dictionary<int, PixelRect> _dirty = [];
    private readonly int _columns;

    /// <param name="width">Layer width in grid pixels.</param>
    /// <param name="height">Layer height in grid pixels.</param>
    /// <param name="originX">Where the grid starts, so a panned layer keeps its tile edges.</param>
    /// <param name="originY">Where the grid starts, vertically.</param>
    /// <param name="tileEdge">Tile edge in pixels; upstream's <c>tileSize</c>.</param>
    public StrokeTiles(int width, int height, double originX = 0, double originY = 0, double tileEdge = TileGrid.StrokeCell)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A layer with no pixels has no tiles to touch.");
        }

        if (tileEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileEdge), "A tile must have a positive edge.");
        }

        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
        TileEdge = tileEdge;

        // Upstream: (width + tileSize - 1) / tileSize, so a part-tile column at the right edge still exists.
        _columns = (int)((width + tileEdge - 1) / tileEdge);
    }

    public int Width { get; }

    public int Height { get; }

    public double OriginX { get; }

    public double OriginY { get; }

    public double TileEdge { get; }

    /// <summary>Tiles allocated so far. This, not the layer's area, is what a stroke costs to paint.</summary>
    public int AllocatedCount => _tiles.Count;

    /// <summary>Tiles with unpublished changes.</summary>
    public int DirtyCount => _dirty.Count;

    /// <summary>
    /// Union of every allocated tile, which is what upstream grows content bounds from: a stroke painted
    /// past the layer's old pixels has to widen the bounds to hold them, and a mask keeps every tile it
    /// touched rather than only the ones with visible paint.
    /// </summary>
    public PixelRect AllocatedBounds
    {
        get
        {
            PixelRect? bounds = null;
            foreach (var tile in _tiles.Values)
            {
                bounds = bounds is { } seen ? seen.Union(tile) : tile;
            }

            return bounds ?? PixelRect.Empty;
        }
    }

    /// <summary>
    /// Records that <paramref name="affected"/> was painted, allocating only the tiles it overlaps.
    /// Returns how many tiles the call touched, which is the number a caller must never be able to grow
    /// into "the whole surface" by accident.
    /// </summary>
    public int Touch(PixelRect affected)
    {
        if (affected.IsEmpty)
        {
            return 0;
        }

        var firstX = (int)Math.Floor((affected.MinX - OriginX) / TileEdge);
        var lastX = (int)Math.Floor((affected.MaxX - OriginX - 1) / TileEdge);
        var firstY = (int)Math.Floor((affected.MinY - OriginY) / TileEdge);
        var lastY = (int)Math.Floor((affected.MaxY - OriginY - 1) / TileEdge);

        var touched = 0;
        for (var y = firstY; y <= lastY; y++)
        {
            for (var x = firstX; x <= lastX; x++)
            {
                var key = y * _columns + x;
                var rect = TileRect(x, y);

                // Clip to the layer: the last row and column are partial, and a tile must never claim
                // pixels the layer does not have.
                var clipped = rect.Intersect(new PixelRect(OriginX, OriginY, Width, Height));
                if (clipped.IsEmpty)
                {
                    continue;
                }

                _tiles[key] = clipped;
                var local = affected.Intersect(clipped);
                if (local.IsEmpty)
                {
                    continue;
                }

                var tileLocal = local.OffsetBy(-(clipped.MinX - OriginX), -(clipped.MinY - OriginY));
                _dirty[key] = _dirty.TryGetValue(key, out var pending) ? pending.Union(tileLocal) : tileLocal;
                touched++;
            }
        }

        return touched;
    }

    /// <summary>Which tiles have changes to republish, as tile-local rects keyed by the same index <see cref="Touch"/> uses.</summary>
    public IReadOnlyDictionary<int, PixelRect> DirtyTiles() => _dirty;

    /// <summary>
    /// The tiles a render pass has to rebuild, in layer pixels. This is the input to
    /// <see cref="TileGrid.Interiors"/>: upstream maps <c>patches.map(\.rect)</c> and nothing else.
    /// </summary>
    public IReadOnlyList<PixelRect> PatchRects()
    {
        var rects = new List<PixelRect>(_dirty.Count);
        foreach (var key in _dirty.Keys)
        {
            rects.Add(_tiles[key]);
        }

        rects.Sort(static (a, b) => a.MinY != b.MinY ? a.MinY.CompareTo(b.MinY) : a.MinX.CompareTo(b.MinX));
        return rects;
    }

    /// <summary>
    /// Hands the dirty set over to a publisher and clears it, the way upstream reads
    /// <c>dirtyTiles[key]</c> and then sets it to nil. Returns the tiles that were published, in layer pixels.
    /// </summary>
    public IReadOnlyList<PixelRect> Publish()
    {
        if (_dirty.Count == 0)
        {
            return [];
        }

        var published = new List<PixelRect>(_dirty.Count);
        foreach (var key in _dirty.Keys)
        {
            published.Add(_tiles[key]);
        }

        _dirty.Clear();
        published.Sort(static (a, b) => a.MinY != b.MinY ? a.MinY.CompareTo(b.MinY) : a.MinX.CompareTo(b.MinX));
        return published;
    }

    /// <summary>The unclipped grid square for a column and row, so tests can name where a tile should be.</summary>
    public PixelRect TileRect(int column, int row) =>
        new(OriginX + (column * TileEdge), OriginY + (row * TileEdge), TileEdge, TileEdge);
}
