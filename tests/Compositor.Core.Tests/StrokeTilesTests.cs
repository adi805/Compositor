using Compositor.Core.Rendering;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// The locality machinery: which tiles a stroke allocates, and which pieces a change of that size has to
/// rebuild. Every expectation here is arithmetic done by hand from upstream's rules, written out in the
/// comment above it, because the whole point of these types is that a count must not quietly become
/// "everything".
/// </summary>
public sealed class StrokeTilesTests
{
    private const double Cell = TileGrid.StrokeCell; // 256

    private static StrokeTiles Layer4096() => new(4096, 4096);

    [Fact]
    public void AStrokeAllocatesOnlyTheSquaresItTouches()
    {
        var tiles = Layer4096();

        // columns = (4096 + 255) / 256 = 16. A dab at (2000,2000) 40x40 spans x [2000,2040):
        // first = floor(2000/256) = 7, last = floor((2040-1)/256) = floor(2039/256) = 7. Same in y,
        // so exactly one tile: column 7, row 7, at (1792,1792).
        var touched = tiles.Touch(new PixelRect(2000, 2000, 40, 40));

        Assert.Equal(1, touched);
        Assert.Equal(1, tiles.AllocatedCount);
        Assert.Equal(new PixelRect(1792, 1792, 256, 256), tiles.TileRect(7, 7));
        Assert.Equal(new PixelRect(1792, 1792, 256, 256), tiles.AllocatedBounds);

        // Tile-local dirty, upstream's convention: (2000-1792, 2000-1792, 40, 40).
        var dirty = Assert.Single(tiles.DirtyTiles());
        Assert.Equal(119, dirty.Key); // y * columns + x = 7 * 16 + 7
        Assert.Equal(new PixelRect(208, 208, 40, 40), dirty.Value);
    }

    [Fact]
    public void ASecondDabInOneTileWidensItsDirtyRectWithoutAddingTiles()
    {
        var tiles = Layer4096();
        tiles.Touch(new PixelRect(2000, 2000, 40, 40)); // tile-local (208,208,40,40)

        tiles.Touch(new PixelRect(1800, 1800, 10, 10)); // tile-local (8,8,10,10), same tile

        Assert.Equal(1, tiles.AllocatedCount);
        Assert.Equal(1, tiles.DirtyCount);
        // Union of (8,8,10,10) and (208,208,40,40): x 8..248, y 8..248.
        Assert.Equal(new PixelRect(8, 8, 240, 240), tiles.DirtyTiles()[119]);
    }

    [Fact]
    public void ADabOnACornerOfFourTilesAllocatesFour()
    {
        var tiles = Layer4096();

        // (250,250,10,10) spans x [250,260): floor(250/256)=0 and floor(259/256)=1, so columns 0 and 1,
        // and the same in y. Four tiles for a 10x10 dab is the cost of sitting on a boundary, and it is
        // still four, not 256.
        Assert.Equal(4, tiles.Touch(new PixelRect(250, 250, 10, 10)));
        Assert.Equal(4, tiles.AllocatedCount);

        // 256-pixel apart in both axes: same key arithmetic, no overlap. 768 lands exactly on a cell
        // boundary and 788 stays inside it, so this one is a single tile.
        Assert.Equal(1, tiles.Touch(new PixelRect(768, 768, 20, 20)));
        Assert.Equal(5, tiles.AllocatedCount);
    }

    [Fact]
    public void APartialTileAtTheFarEdgeNeverClaimsPixelsTheLayerDoesNotHave()
    {
        var tiles = Layer4096();

        // (4000,4000,200,200) reaches x 4200, past the 4096 layer. Cells run to column floor(4199/256)=16
        // and row 16, but columns and rows 16 clip to nothing, so only (15,15) survives.
        var touched = tiles.Touch(new PixelRect(4000, 4000, 200, 200));

        Assert.Equal(1, touched);
        Assert.Equal(new PixelRect(3840, 3840, 256, 256), Assert.Single(tiles.PatchRects()));
    }

    [Fact]
    public void PublishHandsTheDirtySetOverAndKeepsTheTiles()
    {
        var tiles = Layer4096();
        tiles.Touch(new PixelRect(2000, 2000, 40, 40));

        var published = tiles.Publish();

        Assert.Equal(new PixelRect(1792, 1792, 256, 256), Assert.Single(published));
        Assert.Equal(0, tiles.DirtyCount);
        Assert.Empty(tiles.PatchRects());
        // The context stays allocated: publishing is about what changed, not about forgetting the tile.
        Assert.Equal(1, tiles.AllocatedCount);
        Assert.Empty(tiles.Publish());
    }

    [Fact]
    public void TouchingNothingIsNotAnError()
    {
        var tiles = Layer4096();

        Assert.Equal(0, tiles.Touch(PixelRect.Empty));
        Assert.Equal(0, tiles.AllocatedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ALayerWithoutPixelsHasNoTiles(int width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new StrokeTiles(width, 100));
}

/// <summary>
/// Piece geometry, checked against the rules in <c>TiledLayerRenderer.piece</c>: the margin is the support
/// of the level, the region is aligned to the halving step, the bounds cut it, and the pixel cap refuses
/// the piece before anything is allocated.
/// </summary>
public sealed class PieceLayoutTests
{
    private static readonly PixelRect Layer = new(0, 0, 4096, 4096);

    [Fact]
    public void Level0KeightsPixelsOfMarginOnEverySide()
    {
        var piece = PieceLayout.Plan(new PixelRect(1792, 1792, 256, 256), 0, Layer);

        Assert.NotNull(piece);
        // Support(0) = 8, step = 1, so the region is the interior grown by 8 on all sides.
        Assert.Equal(new PixelRect(1784, 1784, 272, 272), piece!.Value.Region);
        Assert.Equal(272L * 272, piece.Value.RegionPixels);
        Assert.Equal((272, 272), piece.Value.ReducedSize);
        // Bounds are set, so the interior is the intersection, which here is unchanged.
        Assert.Equal(new PixelRect(1792, 1792, 256, 256), piece.Value.Interior);
    }

    [Fact]
    public void Level1UsesThirtyTwoPixelsOfMarginAndHalvesOnce()
    {
        var piece = PieceLayout.Plan(new PixelRect(1792, 1792, 256, 256), 1, Layer);

        Assert.NotNull(piece);
        // Support(1) = 16 << 1 = 32, step = 2. Grown: (1760,1760,320,320); both edges already even,
        // so aligning to step 2 does not move them.
        Assert.Equal(new PixelRect(1760, 1760, 320, 320), piece!.Value.Region);
        Assert.Equal((160, 160), piece.Value.ReducedSize);
    }

    [Fact]
    public void TheMarginIsCutAtTheLayerEdgeButTheInteriorSurvives()
    {
        var piece = PieceLayout.Plan(new PixelRect(3840, 3840, 256, 256), 0, Layer);

        Assert.NotNull(piece);
        // Grown reaches 4104, past 4096, so the region is cut there: width and height 4096 - 3832 = 264.
        Assert.Equal(new PixelRect(3832, 3832, 264, 264), piece!.Value.Region);
        Assert.Equal(new PixelRect(3840, 3840, 256, 256), piece.Value.Interior);
    }

    [Fact]
    public void WithoutBoundsThePieceKeepsItsMarginEvenOffGrid()
    {
        var piece = PieceLayout.Plan(new PixelRect(0, 0, 256, 256), 0);

        Assert.NotNull(piece);
        Assert.Equal(new PixelRect(-8, -8, 272, 272), piece!.Value.Region);
        // bounds == nil is the "nothing to clip against" case: upstream keeps the interior whole.
        Assert.Equal(new PixelRect(0, 0, 256, 256), piece.Value.Interior);
    }

    [Fact]
    public void AChangeWhoseRegionFallsOutsideTheLayerPlansNothing()
    {
        Assert.Null(PieceLayout.Plan(new PixelRect(5120, 5120, 256, 256), 0, Layer));
    }

    [Theory]
    [InlineData(7984, true)] // grown to 8000x8000 = 64,000,000 pixels, exactly at the cap
    [InlineData(7985, false)] // grown to 8001x8001 = 64,016,001, one over: refused before allocating
    public void ThePixelCapIsCheckedBeforeAnythingIsAllocated(int interiorEdge, bool expected)
    {
        var piece = PieceLayout.Plan(new PixelRect(0, 0, interiorEdge, interiorEdge), 0);

        Assert.Equal(expected, piece is not null);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)] // DownsampleLevels.MaxLevel is 6
    public void LevelsOutsideTheHalvingChainPlanNothing(int level) =>
        Assert.Null(PieceLayout.Plan(new PixelRect(1792, 1792, 256, 256), level, Layer));

    [Fact]
    public void AStrokeOnAFourKLayerRebuildsNinePiecesNotTwoHundredAndFiftySix()
    {
        // The acceptance number for the whole pipeline. One touched tile (1792,1792,256,256) grown by the
        // support of level 0 spans [1784,2056) on both axes, and 1792 and 2048 are grid boundaries inside
        // that span, so the change reaches 3 cells per axis: 9 pieces. The layer itself is 16x16 = 256
        // cells, so a stroke costs 9/256 of the surface, and it stays that way however big the layer gets.
        var pieces = PieceLayout.Plans([new PixelRect(1792, 1792, 256, 256)], 0, bounds: Layer);

        Assert.Equal(9, pieces.Count);
        Assert.Equal(256, (4096 / (int)TileGrid.StrokeCell) * (4096 / (int)TileGrid.StrokeCell));
    }

    [Fact]
    public void TwoDabsWithDifferentSpansAddUpRatherThanBlowUp()
    {
        var one = PieceLayout.Plans([new PixelRect(1792, 1792, 256, 256)], 0, bounds: Layer);

        // A tile-local 56-pixel span [504,560) crosses only the 512 boundary: 2 cells per axis = 4 pieces.
        var two = PieceLayout.Plans(
            [new PixelRect(1792, 1792, 256, 256), new PixelRect(504, 504, 56, 56)], 0, bounds: Layer);

        Assert.Equal(9, one.Count);
        Assert.Equal(13, two.Count); // 9 + 4, and nothing about the canvas entered the count
    }

    [Fact]
    public void PiecesOutsideWhatTheViewportShowsAreNotBuilt()
    {
        var all = PieceLayout.Plans([new PixelRect(1792, 1792, 256, 256)], 0, bounds: Layer);
        var shown = PieceLayout.Plans(
            [new PixelRect(1792, 1792, 256, 256)], 0, bounds: Layer,
            visible: new PixelRect(1792, 1792, 256, 256));

        // Every neighbour piece touches the visible square only at its edge, and edge contact is not
        // intersection: [1784,1792) against [1792,2048) is empty. Only the square under the stroke is built.
        Assert.Equal(9, all.Count);
        var only = Assert.Single(shown);
        Assert.Equal(new PixelRect(1792, 1792, 256, 256), only.Interior);
    }
}
