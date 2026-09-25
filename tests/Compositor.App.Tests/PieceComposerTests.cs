using Compositor.App.Rendering;
using Compositor.Core;
using Compositor.Core.Rendering;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// The cost claim of the patch pipeline, measured rather than asserted. A 4096x4096 layer is
/// 16,777,216 pixels; a stroke that touches one 256-pixel tile must not resample anything like that many,
/// and the only way to know is to count what the composer actually processed.
/// </summary>
public sealed class PieceComposerTests
{
    /// <summary>A surface whose every pixel is derived from its own coordinates by hand-checkable
    /// arithmetic, so a piece's contents can be predicted without asking the code what it produced.</summary>
    private static RasterSurface Surface(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = ((y * width) + x) * 4;
                pixels[at] = (byte)(x % 256);
                pixels[at + 1] = (byte)(y % 256);
                pixels[at + 2] = (byte)((x + y) % 256);
                pixels[at + 3] = 255;
            }
        }

        return new RasterSurface(width, height, pixels);
    }

    /// <summary>The nine pieces one touched tile at (1792,1792) implies at level 0 (see StrokeTilesTests
    /// for the derivation). The four corner pieces are 24x24, the four edge pieces are 272x24 or 24x272,
    /// and the centre is 272x272: 4(576) + 4(6528) + 73984.</summary>
    private static IReadOnlyList<PieceLayout> OneStrokePiece =>
        PieceLayout.Plans([new PixelRect(1792, 1792, 256, 256)], 0, bounds: new PixelRect(0, 0, 4096, 4096));

    [Fact]
    public void AStrokeOnAFourKLayerResamplesAHundredThousandPixelsNotSeventeenMillion()
    {
        var source = Surface(4096, 4096);
        var composer = new PieceComposer();

        var pieces = composer.Compose(source, OneStrokePiece, level: 0);

        Assert.Equal(9, pieces.Count);
        Assert.Equal(9, composer.PiecesBuilt);
        Assert.Equal(102_400L, composer.PixelsResampled);
        Assert.Equal(16_777_216L, PieceComposer.PixelsForWholeSurface(source));

        // The number the whole task exists for: 0,61 % of a full rebuild.
        Assert.True(
            composer.PixelsResampled * 100 < PieceComposer.PixelsForWholeSurface(source),
            $"a single stroke resampled {composer.PixelsResampled} of {PieceComposer.PixelsForWholeSurface(source)} pixels");
    }

    [Fact]
    public void APieceHoldsExactlyTheSourcePixelsItsRegionCovers()
    {
        var source = Surface(4096, 4096);
        var composer = new PieceComposer();

        var pieces = composer.Compose(source, OneStrokePiece, level: 0);

        // The top-left of the list is the corner piece at region (1776,1776,24,24): sorting is by row then
        // column. Its first pixel is the source at (1776,1776), which by construction is
        // R=1776%256=240, G=240, B=(1776+1776)%256=3552%256=224, A=255.
        var corner = pieces[0];
        Assert.Equal(new PixelRect(1776, 1776, 24, 24), corner.Layout.Region);
        Assert.Equal((24, 24), corner.Layout.ReducedSize);
        Assert.Equal(24 * 24 * 4, corner.Image.Pixels.Length);
        Assert.Equal(240, (int)corner.Image.Pixels[0]);
        Assert.Equal(240, (int)corner.Image.Pixels[1]);
        Assert.Equal(224, (int)corner.Image.Pixels[2]);
        Assert.Equal(255, (int)corner.Image.Pixels[3]);

        // And one from inside the centre piece, hand-derived the same way: region starts at (1784,1784),
        // so its pixel (10,10) is source (1794,1794) -> R=1794%256=2, G=2, B=(1794+1794)%256=3588%256=4.
        var centre = pieces.Single(p => p.Layout.Region == new PixelRect(1784, 1784, 272, 272));
        var at = ((10 * 272) + 10) * 4;
        Assert.Equal(2, (int)centre.Image.Pixels[at]);
        Assert.Equal(2, (int)centre.Image.Pixels[at + 1]);
        Assert.Equal(4, (int)centre.Image.Pixels[at + 2]);
    }

    [Fact]
    public void OneLevelOfHalvingReducesEveryPieceToItsPlannedSize()
    {
        var source = Surface(512, 512);
        var composer = new PieceComposer();
        var pieces = PieceLayout.Plans([new PixelRect(256, 256, 256, 256)], 1, bounds: new PixelRect(0, 0, 512, 512));

        var built = composer.Compose(source, pieces, level: 1);

        Assert.NotEmpty(built);
        foreach (var piece in built)
        {
            var (expectedWidth, expectedHeight) = piece.Layout.ReducedSize;
            Assert.Equal(expectedWidth, piece.Image.Width);
            Assert.Equal(expectedHeight, piece.Image.Height);
            Assert.Equal(expectedWidth * expectedHeight * 4, piece.Image.Pixels.Length);
        }
    }

    [Fact]
    public void AnOversizedPieceIsSkippedInsteadOfAllocated()
    {
        var source = Surface(64, 64);
        var composer = new PieceComposer(pixelBudget: 1000);
        var layout = new PieceLayout(
            new PixelRect(0, 0, 64, 64), new PixelRect(0, 0, 64, 64), 0);

        var built = composer.Compose(source, [layout], level: 0);

        // 64x64 is 4096 pixels, over the 1000 budget: nothing is built, and nothing was resampled.
        Assert.Empty(built);
        Assert.Equal(0, composer.PiecesBuilt);
        Assert.Equal(0, composer.PixelsResampled);
    }

    [Fact]
    public void APlanPointingOffTheSurfaceProducesNoPiece()
    {
        var source = Surface(128, 128);
        var composer = new PieceComposer();
        var layout = new PieceLayout(
            new PixelRect(400, 400, 32, 32), new PixelRect(392, 392, 48, 48), 0);

        Assert.Empty(composer.Compose(source, [layout], level: 0));
    }

    [Fact]
    public void TheCountersResetPerCallSoCostIsAttributableToOneStroke()
    {
        var source = Surface(4096, 4096);
        var composer = new PieceComposer();

        composer.Compose(source, OneStrokePiece, level: 0);
        var afterFirst = composer.PiecesBuilt;
        composer.Compose(source, [], level: 0);

        Assert.Equal(9, afterFirst);
        Assert.Equal(0, composer.PiecesBuilt);
    }
}
