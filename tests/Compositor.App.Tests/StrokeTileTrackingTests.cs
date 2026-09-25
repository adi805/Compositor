using Compositor.Core;
using Compositor.Core.Rendering;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// The stroke path now reports what it painted in tiles, which is the input the renderer's piece rebuild
/// needs. Counted on a 1280x720 document with the default 40 px tip: the grid is 256 px, so columns are
/// floor(x/256) and a dab's footprint is the tip diameter around its centre.
/// </summary>
public sealed class StrokeTileTrackingTests
{
    private static EditorViewModel Vm1280()
    {
        var vm = new EditorViewModel(new Document(1280, 720));
        vm.AddLayer();
        return vm;
    }

    [Fact]
    public void NoStrokeMeansNoPatches()
    {
        var vm = Vm1280();

        Assert.Empty(vm.StrokePatchRects());
    }

    [Fact]
    public void AStrokeInsideOneSquareReportsOneSquare()
    {
        var vm = Vm1280();
        Assert.Equal(40f, vm.BrushDiameter);

        // Dab at (600,300): footprint [580,620)x[280,320) -> column floor(580/256)=2 (and floor(619/256)=2,
        // so it does not cross), row floor(280/256)=1. The move to (640,340) lands in the same square.
        Assert.True(vm.BeginStroke(600, 300));
        vm.ContinueStroke(640, 340);
        Assert.True(vm.EndStroke());

        var patch = Assert.Single(vm.StrokePatchRects());
        Assert.Equal(new PixelRect(512, 256, 256, 256), patch);
    }

    [Fact]
    public void ATipOnACornerOfFourSquaresReportsFour()
    {
        var vm = Vm1280();

        // (512,512) with a 40 px tip spans [492,532) on both axes, crossing the 512 boundary in each,
        // so four squares are dirty. That is the cost of straddling an edge, and it is four, not "the layer".
        Assert.True(vm.BeginStroke(512, 512));
        Assert.True(vm.EndStroke());

        var patches = vm.StrokePatchRects();
        Assert.Equal(4, patches.Count);
        Assert.Contains(new PixelRect(256, 256, 256, 256), patches);
        // The bottom row is partial on a 720 px tall document: clipped to 208 px, never claiming
        // rows the layer does not have.
        Assert.Contains(new PixelRect(512, 512, 256, 208), patches);
    }

    [Fact]
    public void TwoDistantStrokesAccumulateSquaresAndNotTheCanvas()
    {
        var vm = Vm1280();

        Assert.True(vm.BeginStroke(600, 300));
        vm.EndStroke();
        Assert.True(vm.BeginStroke(700, 600));
        vm.EndStroke();

        // Each stroke starts a fresh grid, which is the point: the renderer rebuilds what the current
        // stroke touched, and a second stroke does not inherit the first one's squares.
        var patch = Assert.Single(vm.StrokePatchRects());
        Assert.Equal(new PixelRect(512, 512, 256, 208), patch); // 208, because the layer ends at y 720
    }

    [Fact]
    public void PatchesFeedTheSamePlanTheComposerMeasures()
    {
        var vm = Vm1280();
        Assert.True(vm.BeginStroke(600, 300));
        vm.ContinueStroke(640, 340);
        vm.EndStroke();

        var rects = vm.StrokePatchRects();
        var pieces = PieceLayout.Plans(rects, 0, bounds: new PixelRect(0, 0, 1280, 720));

        // The dirty square (512,256,256,256) grown by the 8 px support spans [504,776) x [248,520).
        // That crosses the 512 and 768 boundaries on x and the 256 and 512 boundaries on y, so it reaches
        // 3 columns and 3 rows: 9 pieces. The document itself is 5 x 3 = 15 squares, which is why the
        // interesting version of this measurement is the 4096x4096 one in PieceComposerTests.
        Assert.Equal(9, pieces.Count);
    }
}
