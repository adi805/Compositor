using Compositor.Core.Rendering;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Tile selection for the tiled layer renderer. Every expected rectangle here is derived by hand from the
/// upstream rule (<c>support</c>, <c>aligned</c>, the <c>size</c> squares grown by <c>margin</c>), never
/// recorded from a run.
/// </summary>
public class TileGridTests
{
    private static PixelRect R(double x, double y, double w, double h) => new(x, y, w, h);

    [Theory]
    [InlineData(0, 8)]
    [InlineData(1, 32)]
    [InlineData(2, 64)]
    [InlineData(3, 128)]
    [InlineData(4, 256)]
    [InlineData(5, 512)]
    [InlineData(6, 1024)]
    public void Support_FollowsTheRule(int level, double expected)
        => Assert.Equal(expected, TileGrid.Support(level));

    [Fact]
    public void Support_AtMaxLevel_FitsInsideOneCommittedCell()
    {
        // A level-6 piece's margin is exactly the committed cell, which is why the cell size can be 1024.
        Assert.Equal(TileGrid.CommittedCell, TileGrid.Support(DownsampleLevels.MaxLevel));
        Assert.True(TileGrid.StrokeCell > 0);
    }

    [Theory]
    [InlineData(3, 3, 10, 10, 8, 0, 0, 0, 0, 16, 16)]
    [InlineData(0, 0, 8, 8, 8, 0, 0, 0, 0, 8, 8)]
    [InlineData(6, 6, 4, 4, 8, 5, 5, 5, 5, 8, 8)]
    [InlineData(105, 10, 10, 10, 8, 100, 0, 100, 8, 16, 16)]
    public void Aligned_SnapsOutwardsToTheStepGrid(
        double x, double y, double w, double h,
        double step, double originX, double originY,
        double ex, double ey, double ew, double eh)
        => Assert.Equal(R(ex, ey, ew, eh), TileGrid.Aligned(R(x, y, w, h), step, originX, originY));

    [Fact]
    public void Interiors_OneRectInsideOneCell_SelectsThatCell()
    {
        var found = TileGrid.Interiors([R(10, 10, 20, 20)], margin: 0, size: 256, step: 1, 0, 0, visible: null);

        Assert.Equal([R(10, 10, 20, 20)], found);
    }

    [Fact]
    public void Interiors_RectSpanningTwoCells_SelectsBoth()
    {
        // x 200..300 crosses the 256 boundary, so it needs cells 0 and 1; y 10..30 stays in row 0.
        var found = TileGrid.Interiors([R(200, 10, 100, 20)], margin: 0, size: 256, step: 1, 0, 0, visible: null);

        Assert.Equal([R(200, 10, 56, 20), R(256, 10, 44, 20)], found);
    }

    [Fact]
    public void Interiors_MarginPullsInTheNeighbouringCells()
    {
        var tight = TileGrid.Interiors([R(250, 10, 2, 2)], margin: 0, size: 256, step: 1, 0, 0, visible: null);
        var grown = TileGrid.Interiors([R(250, 10, 2, 2)], margin: 16, size: 256, step: 1, 0, 0, visible: null);

        // Without a margin the dab sits inside one cell. With margin 16 it grows to 234..268 by -6..28, and
        // because the dab is near the origin corner that crosses the cell boundary on BOTH axes, so four
        // squares have to be rebuilt rather than one.
        Assert.Single(tight);
        Assert.Equal(R(250, 10, 2, 2), tight[0]);
        Assert.Equal(4, grown.Count);
        Assert.Contains(R(234, 0, 22, 28), grown);
        Assert.Contains(R(256, 0, 12, 28), grown);
    }

    [Fact]
    public void Interiors_MarginInsideOneCell_StaysOnePiece()
    {
        // Same dab, moved well clear of every 256 boundary, so a 16 px margin stays in one square.
        var grown = TileGrid.Interiors([R(600, 600, 2, 2)], margin: 16, size: 256, step: 1, 0, 0, visible: null);

        Assert.Single(grown);
        Assert.Equal(R(584, 584, 34, 34), grown[0]);
    }

    [Fact]
    public void Interiors_VisibleFilterDropsWhatCannotBeSeen()
    {
        var shown = R(300, 0, 100, 100);

        Assert.Empty(TileGrid.Interiors([R(10, 10, 20, 20)], 0, 256, 1, 0, 0, shown));
        Assert.Single(TileGrid.Interiors([R(320, 10, 20, 20)], 0, 256, 1, 0, 0, shown));
    }

    [Fact]
    public void Interiors_TwoRectsInOneCell_UnionIntoOnePiece()
    {
        var found = TileGrid.Interiors([R(10, 10, 10, 10), R(100, 100, 10, 10)], 0, 256, 1, 0, 0, null);

        Assert.Equal([R(10, 10, 100, 100)], found);
    }

    [Fact]
    public void Interiors_GrowsThePartToTheHalvingGrid()
    {
        // Step 32 is the level-5 grid: a 20 px part at (10,10) has to become the 32 px block at (0,0).
        var found = TileGrid.Interiors([R(10, 10, 20, 20)], 0, 256, 32, 0, 0, null);

        Assert.Equal([R(0, 0, 32, 32)], found);
    }

    [Fact]
    public void Interiors_RespectsTheLayerOrigin()
    {
        // With origin 100 the 256 px squares start at 100, so 260..280 is still inside the first square.
        var found = TileGrid.Interiors([R(260, 10, 20, 20)], 0, 256, 1, 100, 0, null);

        Assert.Equal([R(260, 10, 20, 20)], found);
    }

    [Fact]
    public void Interiors_EmptyInput_YieldsNothing()
    {
        Assert.Empty(TileGrid.Interiors([], 0, 256, 1, 0, 0, null));
        Assert.Empty(TileGrid.Interiors([R(10, 10, 0, 0)], 0, 256, 1, 0, 0, null));
    }

    [Fact]
    public void Interiors_BigCanvasStroke_SelectsCellsForTheStrokeNotTheWholeSurface()
    {
        // 4096x4096 at the 256 stroke cell is 16x16 = 256 squares. A 100 px dab at (2000,2000) grown by the
        // level-0 support (8) covers 1992..2108, which touches cells 7 and 8 on each axis: four squares, a
        // sixteenth of the surface. This is the behaviour that keeps a stroke on a big canvas cheap.
        var found = TileGrid.Interiors(
            [R(2000, 2000, 100, 100)],
            margin: TileGrid.Support(0),
            size: TileGrid.StrokeCell,
            step: 1,
            originX: 0,
            originY: 0,
            visible: null);

        const int squaresAcross = 4096 / 256;
        const int totalSquares = squaresAcross * squaresAcross;
        Assert.Equal(256, totalSquares);
        Assert.Equal(4, found.Count);
        Assert.True(found.Count * 16 < totalSquares, "a dab must rebuild a small fraction of the surface");
        Assert.All(found, piece => Assert.True(piece.Width <= TileGrid.StrokeCell && piece.Height <= TileGrid.StrokeCell));
    }

    [Fact]
    public void Interiors_PiecesStayInsideTheirOwnSquare()
    {
        var found = TileGrid.Interiors([R(10, 10, 500, 20)], 0, 256, 1, 0, 0, null);

        // 10..510 reaches 510, and ceil(510/256) = 2, so only cells 0 and 1 are touched. Each piece is clipped
        // to its own 256 px square, so the two together cover exactly the original span with no overlap.
        Assert.Equal([R(10, 10, 246, 20), R(256, 10, 254, 20)], found);
        Assert.Equal(510, found[1].MaxX);
        Assert.Equal(500, found[0].Width + found[1].Width);
    }

    [Fact]
    public void PixelRect_GeometryHelpers()
    {
        Assert.Equal(R(10, 10, 20, 20), R(15, 15, 10, 10).Grow(5));
        Assert.Equal(R(5, 5, 30, 30), R(10, 10, 20, 20).Grow(5));
        Assert.True(R(0, 0, 10, 10).Intersects(R(5, 5, 10, 10)));
        Assert.False(R(0, 0, 10, 10).Intersects(R(20, 20, 5, 5)));
        Assert.Equal(R(5, 5, 5, 5), R(0, 0, 10, 10).Intersect(R(5, 5, 10, 10)));
        Assert.True(R(0, 0, 10, 10).Intersect(R(20, 20, 5, 5)).IsEmpty);
        Assert.Equal(R(0, 0, 20, 20), R(0, 0, 10, 10).Union(R(10, 10, 10, 10)));
        Assert.Equal(R(5, 5, 10, 10), PixelRect.Empty.Union(R(5, 5, 10, 10)));
        Assert.True(R(0, 0, 10, 10).Contains(R(2, 2, 5, 5)));
    }
}
