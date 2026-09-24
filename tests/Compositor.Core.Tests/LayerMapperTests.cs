using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// The mapper is what keeps the brush and the renderer telling the same story, so these
/// expectations are taken from the hand-computed placement goldens in LayerPlacementTests
/// plus the round-trip property, never from whatever the code happens to return.
/// </summary>
public class LayerMapperTests
{
    private const int Size = 8;

    private static LayerTransform Placed(double x, double y, double w, double h, double degrees = 0) =>
        new(x, y, w, h, degrees, false, false);

    [Fact]
    public void Identity_MapsEveryPointOntoItself()
    {
        var mapper = LayerPlacement.MapFor(LayerTransform.ForCanvas(Size, Size), Size, Size, Size, Size);
        foreach (var (x, y) in new[] { (0.5, 0.5), (4d, 4d), (7.5, 7.5), (3.25, 6.75) })
        {
            var (lx, ly) = mapper.ToLayer(x, y);
            Assert.Equal(x, lx, 9);
            Assert.Equal(y, ly, 9);
        }
    }

    [Fact]
    public void MovedLayer_PutsTheDocumentPointBackWhereTheInkWasLaid()
    {
        // LayerPlacementTests.Move_SlidesTheArtwork: texel (5,1) of a rect-(2,3) layer
        // renders at document pixel (7,4), so its centre (7.5,4.5) maps back to (5.5,1.5).
        var mapper = LayerPlacement.MapFor(Placed(2, 3, Size, Size), Size, Size, Size, Size);
        var (lx, ly) = mapper.ToLayer(7.5, 4.5);
        Assert.Equal(5.5, lx, 9);
        Assert.Equal(1.5, ly, 9);
    }

    [Fact]
    public void ForwardAndBack_AreEachOthersInverse_ForEveryPlacement()
    {
        var transforms = new[]
        {
            Placed(0, 0, Size, Size),
            Placed(-13, 7, Size, Size),
            Placed(2, 2, 4, 4),
            Placed(-4, -4, 16, 16, 90),
            Placed(1, -2, 12, 5, 37),
            new LayerTransform(0, 0, 0, 0, 15, false, false), // cover-canvas, turned
        };

        foreach (var transform in transforms)
        {
            var mapper = LayerPlacement.MapFor(transform, Size, Size, Size, Size);
            Assert.True(mapper.IsValid);
            foreach (var point in new[]
            {
                (X: 1.5, Y: 2.5), (X: 4d, Y: 4d), (X: 0.25, Y: 7.75), (X: 6d, Y: 3d),
            })
            {
                var doc = mapper.ToDocument(point.X, point.Y);
                var back = mapper.ToLayer(doc.X, doc.Y);
                Assert.Equal(point.X, back.X, 9);
                Assert.Equal(point.Y, back.Y, 9);
            }
        }
    }

    [Fact]
    public void HalfScale_HalvesTheDistanceFromThePivot()
    {
        // Rect (2,2,4,4) over an 8x8 source: scale 0.5 about centre (4,4), so document
        // point (5.5, 5.5) sits 1.5 doc px out from the pivot = 3 layer px in.
        var mapper = LayerPlacement.MapFor(Placed(2, 2, 4, 4), Size, Size, Size, Size);
        var (lx, ly) = mapper.ToLayer(5.5, 5.5);
        Assert.Equal(7d, lx, 9);
        Assert.Equal(7d, ly, 9);
    }

    private static byte[] MaskWithBlock(int x, int y, int w, int h)
    {
        var mask = new byte[Size * Size];
        for (var row = y; row < y + h; row++)
        {
            for (var col = x; col < x + w; col++)
            {
                mask[(row * Size) + col] = 255;
            }
        }

        return mask;
    }

    [Fact]
    public void PlaceMask_MovesTheSelectionBackIntoLayerSpace()
    {
        // A doc mask selected at [4,6)x[4,6) on a layer shifted by (+2,+3): the layer's own
        // pixels showing through that region are x in [2,4) and y in [1,3).
        var layerMask = LayerPlacement.PlaceMask(
            Placed(2, 3, Size, Size), MaskWithBlock(4, 4, 2, 2), Size, Size, Size, Size);

        Assert.Equal(255, layerMask[(1 * Size) + 2]);
        Assert.Equal(255, layerMask[(2 * Size) + 3]);
        Assert.Equal(0, layerMask[(1 * Size) + 1]);
        Assert.Equal(0, layerMask[(3 * Size) + 3]);
        Assert.Equal(0, layerMask[(4 * Size) + 4]);
    }

    [Fact]
    public void PlaceMask_KeepsAnUntransformedSelectionByteForByte()
    {
        var docMask = MaskWithBlock(1, 2, 3, 4);
        var same = LayerPlacement.PlaceMask(
            LayerTransform.ForCanvas(Size, Size), docMask, Size, Size, Size, Size);
        Assert.Equal(docMask, same);
    }

    [Fact]
    public void PlaceMask_DropsWhatFallsOffTheCanvas()
    {
        // Shifted right by 3: layer columns 5..7 look past the canvas edge and stay empty.
        var layerMask = LayerPlacement.PlaceMask(
            Placed(3, 0, Size, Size), MaskWithBlock(0, 0, Size, Size), Size, Size, Size, Size);

        Assert.Equal(255, layerMask[0]); // column 0 reads doc column 3
        Assert.Equal(0, layerMask[(2 * Size) + 7]);
        Assert.Equal(0, layerMask[(6 * Size) + 6]);
    }

    [Fact]
    public void PlaceMask_RejectsAMaskThatIsNotTheCanvas()
    {
        Assert.Throws<ArgumentException>(() => LayerPlacement.PlaceMask(
            LayerTransform.ForCanvas(Size, Size), new byte[9], Size, Size, Size, Size));
    }

    [Fact]
    public void DegenerateSides_AreNotValidMappings()
    {
        var mapper = LayerPlacement.MapFor(new LayerTransform(0, 0, 0, 0, 0, false, false), 0, 0, Size, Size);
        Assert.False(mapper.IsValid);
    }
}
