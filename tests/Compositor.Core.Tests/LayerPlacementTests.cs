using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Layer transforms have to move pixels, and every expectation here is computed by hand
/// from the mapping dest = rectCentre + R(clockwise) * (src - srcCentre) * scale, not read
/// back out of the engine. Several cases land on an exact texel centre, so they are golden
/// values with no tolerance at all.
/// </summary>
public class LayerPlacementTests
{
    private const int Size = 8;

    private static byte[] Marker(int x, int y, byte r = 255, byte g = 0, byte b = 0)
    {
        var pixels = new byte[Size * Size * 4];
        var i = (y * Size + x) * 4;
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
        pixels[i + 3] = 255;
        return pixels;
    }

    private static byte[] Solid(byte r, byte g, byte b, byte a)
    {
        var pixels = new byte[Size * Size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = a;
        }

        return pixels;
    }

    private static (byte R, byte G, byte B, byte A) At(byte[] pixels, int x, int y)
    {
        var i = (y * Size + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }

    private static LayerTransform Placed(double x, double y, double w, double h, double degrees = 0) =>
        new(x, y, w, h, degrees, false, false);

    private static byte[] Place(LayerTransform transform, byte[] source) =>
        LayerPlacement.Place(transform, source, Size, Size, Size, Size);

    [Fact]
    public void Identity_HandsBackTheCallersOwnBuffer()
    {
        var source = Marker(3, 1);
        foreach (var transform in new[]
        {
            LayerTransform.ForCanvas(Size, Size),
            new LayerTransform(0, 0, 0, 0, 0, false, false), // cover-canvas blank layer
        })
        {
            Assert.Same(source, Place(transform, source));
        }
    }

    [Fact]
    public void CoverCanvas_WithAnOffset_StillMoves()
    {
        // Width/Height 0 means "fill my canvas", so a shifted cover-canvas layer is a move.
        var moved = Place(new LayerTransform(2, 3, 0, 0, 0, false, false), Marker(5, 1));
        Assert.Equal((255, 0, 0, 255), At(moved, 7, 4));
    }

    [Fact]
    public void Move_SlidesTheArtworkByExactlyTheOrigin()
    {
        // Rect (2,3,8,8) has its centre at (6,7); src (5,1) sits at (+1,-3) from the
        // source centre, so it lands on dest centre (7.5,4.5) - pixel (7,4), exact texel.
        var moved = Place(Placed(2, 3, Size, Size), Marker(5, 1));

        Assert.Equal((255, 0, 0, 255), At(moved, 7, 4));
        Assert.Equal((byte)0, At(moved, 6, 4).A); // one step back along x
        Assert.Equal((byte)0, At(moved, 7, 3).A); // one step back along y
        Assert.Equal((byte)0, At(moved, 0, 0).A); // and the vacated corner is empty
    }

    [Fact]
    public void Move_DoesNotRewriteTheSource()
    {
        var source = Marker(0, 0);
        Place(Placed(4, 4, Size, Size), source);
        Assert.Equal((255, 0, 0, 255), At(source, 0, 0));
    }

    [Fact]
    public void HalfScale_FillsItsRectAndStopsAtItsEdge()
    {
        // An opaque 8x8 source drawn into rect (2,2,4,4) has centre (4,4) and scale 0.5.
        // Pixel (2,2): u = (2.5-4)/0.5 + 4 - 0.5 = 0.5 -> inside the artwork, covered.
        // Pixel (1,1): u = -1.5 -> a whole texel outside, empty. Pixel (6,6): u = 8.5, empty.
        var placed = Place(Placed(2, 2, 4, 4), Solid(0, 200, 0, 255));

        Assert.Equal((0, 200, 0, 255), At(placed, 2, 2));
        Assert.Equal((0, 200, 0, 255), At(placed, 3, 3));
        Assert.Equal((0, 200, 0, 255), At(placed, 5, 5));
        Assert.Equal((byte)0, At(placed, 1, 1).A);
        Assert.Equal((byte)0, At(placed, 6, 6).A);
        Assert.Equal((byte)0, At(placed, 7, 0).A);
    }

    [Fact]
    public void Rotate90_DegreesTurnsClockwise()
    {
        // Top-left of the source goes to the top-RIGHT for a clockwise quarter turn:
        // dest (7,0) centre is (+3.5,-3.5) from the pivot, inverse-rotated that is
        // (3.5*cos90 + -3.5*sin90, ...) = (-3.5, +3.5)?? No: rx = 0*3.5 + 1*(-3.5) = -3.5,
        // ry = -1*3.5 + 0*(-3.5) = -3.5, giving u = v = 0.0 -> exactly texel (0,0).
        var turned = Place(Placed(0, 0, Size, Size, 90), Marker(0, 0));

        Assert.Equal((255, 0, 0, 255), At(turned, 7, 0));
        Assert.Equal((byte)0, At(turned, 0, 0).A);
        Assert.Equal((byte)0, At(turned, 7, 7).A);
        Assert.Equal((byte)0, At(turned, 0, 7).A);

        // The corner that started top-right ends bottom-right: same handedness.
        var turnedAgain = Place(Placed(0, 0, Size, Size, 90), Marker(7, 0));
        Assert.Equal((255, 0, 0, 255), At(turnedAgain, 7, 7));
    }

    [Fact]
    public void Rotate180_SwapsCorners()
    {
        var turned = Place(Placed(0, 0, Size, Size, 180), Marker(0, 0));
        Assert.Equal((255, 0, 0, 255), At(turned, 7, 7));
        Assert.Equal((byte)0, At(turned, 0, 0).A);
    }

    [Fact]
    public void Rotate_ScalesAndMovesTogether()
    {
        // Rect (-4,-4,16,16): centre (4,4), scale 2, turned 90 clockwise.
        // dest (0,0) centre is (-3.5,-3.5) from the pivot; inverse rotation gives
        // (-3.5, +3.5), /2 -> (-1.75, 1.75), + 4 - 0.5 -> u = 1.75, v = 5.25.
        // So texel (2,5) carries (0.75 * 0.75) = 0.5625 of the marker: alpha 143.
        var placed = Place(Placed(-4, -4, 16, 16, 90), Marker(2, 5));

        Assert.Equal((byte)143, At(placed, 0, 0).A);
        Assert.Equal(255, At(placed, 0, 0).R); // hue survives the blend
        Assert.Equal((byte)0, At(placed, 7, 0).A);
    }

    [Fact]
    public void BlendingAcrossAnAlphaEdge_UsesPremultipliedColour()
    {
        // Left half red, right half empty, magnified 2x into rect (0,0,16,16) whose
        // centre is (8,8). dest (7,7) centre is (-0.5,-0.5) -> u = v = 3.25, so the
        // column weights are 0.75 red / 0.25 empty: alpha 191, hue still pure red.
        // Averaging straight-alpha channels would give R = 64 - the scaling fringe.
        var source = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size / 2; x++)
            {
                var i = (y * Size + x) * 4;
                source[i] = 255;
                source[i + 3] = 255;
            }
        }

        var (r, g, b, a) = At(Place(Placed(0, 0, 16, 16), source), 7, 7);

        Assert.Equal((byte)191, a);
        Assert.Equal((byte)255, r);
        Assert.Equal((byte)0, g);
        Assert.Equal((byte)0, b);
    }

    [Fact]
    public void FullyTransparentSource_StaysEmpty()
    {
        var placed = Place(Placed(1, 1, 4, 4, 33), new byte[Size * Size * 4]);
        Assert.All(placed, bucket => Assert.Equal(0, bucket));
    }

    [Fact]
    public void EmptySource_PlacesNothing()
    {
        // A zero-sized surface has no pixels to map; the rect is not a degenerate case
        // because Width/Height of 0 means "cover the canvas" upstream.
        var placed = LayerPlacement.Place(
            LayerTransform.ForCanvas(Size, Size), Array.Empty<byte>(), 0, 0, Size, Size);
        Assert.All(placed, bucket => Assert.Equal(0, bucket));
    }

    [Fact]
    public void WrongSizedBuffer_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LayerPlacement.Place(LayerTransform.ForCanvas(Size, Size), new byte[7], Size, Size, Size, Size));
    }

    [Fact]
    public void Resolve_ReportsTheCoverCanvasRect()
    {
        Assert.Equal((0d, 0d, 8d, 8d), LayerPlacement.Resolve(new LayerTransform(0, 0, 0, 0, 0, false, false), Size, Size));
        Assert.Equal((1d, 2d, 4d, 4d), LayerPlacement.Resolve(Placed(1, 2, 4, 4), Size, Size));
    }

    [Fact]
    public void Flatten_RendersTheTransformedLayer()
    {
        // End to end through the export path: marker (0,0) shifted by (+2,+2).
        var doc = new Document(Size, Size);
        doc.AddLayer(new Layer("moved")
        {
            Pixels = new RasterSurface(Size, Size, Marker(0, 0)),
            Transform = Placed(2, 2, Size, Size),
        });

        var (width, height, rgba) = Flatten.ToRgba(doc);

        Assert.Equal(Size, width);
        Assert.Equal(Size, height);
        Assert.Equal((255, 0, 0, 255), At(rgba, 2, 2));
        Assert.Equal((byte)0, At(rgba, 0, 0).A);
    }

    [Fact]
    public void CompositeStack_RendersTheTransformedLayer()
    {
        // Rect (-5,-5,8,8) has centre (-1,-1): src (7,7) -> dest (2.5,2.5) -> pixel (2,2).
        var layer = new Layer("moved")
        {
            Pixels = new RasterSurface(Size, Size, Marker(7, 7)),
            Transform = Placed(-5, -5, Size, Size),
        };

        var merged = SurfaceOps.CompositeStack(new[] { layer }, Size, Size);

        Assert.Equal((255, 0, 0, 255), At(merged.Pixels, 2, 2));
        Assert.Equal((byte)0, At(merged.Pixels, 7, 7).A);
    }
}
