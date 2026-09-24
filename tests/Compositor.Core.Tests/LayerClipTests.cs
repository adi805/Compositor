using Compositor.Core;
using Compositor.Core.Imaging;
using Compositor.Core.Selection;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// A selection is drawn on the canvas, but a transformed layer is painted through its own
/// pixel space, so the clip has to travel the same way the brush does. Expectations are
/// derived by hand from the placement rect, not from what the code returned.
/// </summary>
public class LayerClipTests
{
    private const int Size = 8;

    private static LayerTransform Placed(double x, double y, double w, double h, double degrees = 0) =>
        new(x, y, w, h, degrees, false, false);

    private static SelectionClip Block(int x, int y, int w, int h, byte value = 255)
    {
        var coverage = new byte[w * h];
        Array.Fill(coverage, value);
        return new SelectionClip(x, y, w, h, coverage);
    }

    [Fact]
    public void ShiftedLayer_ClipMovesBackByTheSameAmount()
    {
        // Doc coverage at [4,6)x[4,6) on a layer shifted (+2,+3): the layer pixels that
        // show through it are x in [2,4) and y in [1,3).
        var mapped = LayerPlacement.MapClipToLayer(
            Placed(2, 3, Size, Size), Block(4, 4, 2, 2), Size, Size, Size, Size);

        Assert.Equal(0, mapped.X);
        Assert.Equal(0, mapped.Y);
        Assert.Equal(Size, mapped.Width);
        Assert.Equal(1f, mapped.FactorAt(2, 1), 3);
        Assert.Equal(1f, mapped.FactorAt(3, 2), 3);
        Assert.Equal(0f, mapped.FactorAt(2, 3), 3);
        Assert.Equal(0f, mapped.FactorAt(1, 1), 3);
    }

    [Fact]
    public void PartialCoverage_SurvivesTheTripUnscaled()
    {
        // One soft pixel at doc (4,4) with coverage 128 lands on layer (2,1), same weight.
        var clip = new SelectionClip(4, 4, 2, 2, new byte[] { 128, 0, 0, 0 });
        var mapped = LayerPlacement.MapClipToLayer(Placed(2, 3, Size, Size), clip, Size, Size, Size, Size);

        Assert.Equal(128f / 255f, mapped.FactorAt(2, 1), 3);
        Assert.Equal(0f, mapped.FactorAt(3, 1), 3);
    }

    [Fact]
    public void UntransformedLayer_KeepsTheClipWhereItWas()
    {
        var mapped = LayerPlacement.MapClipToLayer(
            LayerTransform.ForCanvas(Size, Size), Block(4, 4, 2, 2), Size, Size, Size, Size);

        Assert.Equal(1f, mapped.FactorAt(4, 4), 3);
        Assert.Equal(1f, mapped.FactorAt(5, 5), 3);
        Assert.Equal(0f, mapped.FactorAt(3, 4), 3);
        Assert.Equal(0f, mapped.FactorAt(6, 6), 3);
    }

    [Fact]
    public void ClipWithoutCoverage_StillTouchesNothing()
    {
        var mapped = LayerPlacement.MapClipToLayer(
            Placed(1, 1, Size, Size), new SelectionClip(0, 0, Size, Size, null), Size, Size, Size, Size);

        Assert.Same(SelectionClip.Empty, mapped);
    }

    [Fact]
    public void RotatedLayer_ClipTurnsWithIt()
    {
        // A quarter turn about the canvas centre maps layer (x,y) to document (4-(y-4), 4+(x-4)).
        // Inverting that, the selected document block [4,8)x[0,4) becomes layer [0,4)x[0,4):
        // layer (1,1) reads document (6,1), inside; layer (6,1) reads document (6,6), outside.
        var selected = Block(4, 0, 4, 4);
        var mapped = LayerPlacement.MapClipToLayer(Placed(0, 0, Size, Size, 90), selected, Size, Size, Size, Size);

        Assert.Equal(1f, mapped.FactorAt(1, 1), 3);
        Assert.Equal(0f, mapped.FactorAt(6, 1), 3);
    }
}
