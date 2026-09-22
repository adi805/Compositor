using Compositor.Core;
using Compositor.Core.Tools;
using Xunit;

namespace Compositor.Core.Tests;

public class ToolTests
{
    // --- Gradient ---

    [Fact]
    public void GradientLinear_InterpolatesAlongAxis()
    {
        var surface = new RasterSurface(10, 2);
        GradientFill.Apply(surface, 0, 1, 10, 1, 255, 0, 0, 0, 0, 255,
            new GradientSettings { Shape = GradientShape.Linear, Style = GradientStyle.ForegroundToBackground });

        // Left edge: pixel center t=0.05 -> r 242, b 13; middle: halfway.
        var (r0, g0, b0, a0) = surface.GetPixel(0, 1);
        Assert.InRange(r0, 236, 255);
        Assert.Equal(0, g0);
        Assert.InRange(b0, 0, 19);
        Assert.Equal(255, a0);

        var (rm, _, bm, _) = surface.GetPixel(5, 1);
        Assert.InRange(rm, 110, 140); // t=0.55 -> 114.75
        Assert.InRange(bm, 115, 145);
    }

    private static int g0(byte v) => 0;

    [Fact]
    public void GradientTransparent_FadesAlpha()
    {
        var surface = new RasterSurface(10, 1);
        GradientFill.Apply(surface, 0, 0, 10, 0, 255, 0, 0, 0, 0, 0,
            new GradientSettings { Style = GradientStyle.ForegroundToTransparent });

        var (_, _, _, startA) = surface.GetPixel(0, 0);   // pixel center t=0.05 -> ~242
        Assert.InRange(startA, 236, 255);
        var (_, _, _, midA) = surface.GetPixel(5, 0);     // ~127.5
        Assert.InRange(midA, 115, 140);
        Assert.Equal(0, surface.GetPixel(10, 0).A);       // past the end: fully transparent
    }

    [Fact]
    public void GradientReversed_SwapsStops()
    {
        var a = new RasterSurface(10, 1);
        var b = new RasterSurface(10, 1);
        var settings = new GradientSettings { Style = GradientStyle.ForegroundToBackground };
        GradientFill.Apply(a, 0, 0, 10, 0, 255, 0, 0, 0, 0, 255, settings);
        GradientFill.Apply(b, 0, 0, 10, 0, 255, 0, 0, 0, 0, 255, settings with { Reversed = true });

        Assert.InRange(a.GetPixel(0, 0).R, 236, 255); // normal: red at start (t=0.05)
        Assert.InRange(b.GetPixel(0, 0).B, 236, 255); // reversed: blue at start
    }

    [Fact]
    public void GradientRadial_CenterToRim()
    {
        var surface = new RasterSurface(21, 21);
        GradientFill.Apply(surface, 10, 10, 20, 10, 255, 0, 0, 0, 0, 0,
            new GradientSettings { Shape = GradientShape.Radial, Style = GradientStyle.ForegroundToBackground });

        Assert.InRange(surface.GetPixel(10, 10).R, 230, 255); // center (t=0.0707 -> 237)
        Assert.Equal(0, surface.GetPixel(20, 10).R);   // rim
    }

    [Fact]
    public void Gradient_TooShortLine_NoOp()
    {
        var surface = new RasterSurface(4, 4);
        GradientFill.Apply(surface, 2, 2, 2.2f, 2, 255, 0, 0, 0, 0, 0, new GradientSettings());
        Assert.Equal(0, surface.GetPixel(0, 0).A);
    }

    // --- Shape ---

    [Fact]
    public void ShapeRectangle_FillsExactBounds()
    {
        var surface = new RasterSurface(20, 20);
        ShapeRasterizer.Fill(surface, 5, 5, 10, 10, ShapeKind.Rectangle, 0, 255, 0, 0, 1f);

        Assert.Equal(255, surface.GetPixel(5, 5).A);
        Assert.Equal(255, surface.GetPixel(14, 14).A);
        Assert.Equal(0, surface.GetPixel(4, 4).A);
        Assert.Equal(0, surface.GetPixel(15, 15).A);
    }

    [Fact]
    public void ShapeEllipse_CornersEmpty_CenterFull()
    {
        var surface = new RasterSurface(40, 40);
        ShapeRasterizer.Fill(surface, 0, 0, 40, 40, ShapeKind.Ellipse, 0, 0, 255, 0, 1f);

        Assert.Equal(255, surface.GetPixel(20, 20).A);  // center
        Assert.Equal(0, surface.GetPixel(1, 1).A);      // corner
        Assert.Equal(255, surface.GetPixel(20, 2).A);   // top edge midpoint area (y=2 inside)
    }

    [Fact]
    public void ShapeRoundedRectangle_PillCornerEmpty()
    {
        var surface = new RasterSurface(20, 20);
        ShapeRasterizer.Fill(surface, 0, 0, 20, 20, ShapeKind.Rectangle, 10, 255, 255, 0, 1f); // full pill

        Assert.Equal(0, surface.GetPixel(1, 1).A);      // sharp corner cut
        Assert.Equal(255, surface.GetPixel(10, 1).A);   // top middle
        Assert.Equal(255, surface.GetPixel(10, 10).A);  // center
    }

    // --- Blur ---

    [Fact]
    public void BlurSigma_FollowsDiameterClamped()
    {
        Assert.Equal(1.5f, BlurStroke.SigmaFor(4f));    // min clamp
        Assert.Equal(3f, BlurStroke.SigmaFor(30f));     // diameter/10
        Assert.Equal(30f, BlurStroke.SigmaFor(900f));   // max clamp
    }

    [Fact]
    public void BlurSharpEdge_Softens()
    {
        // 10x10: left half opaque red, right half transparent.
        var surface = new RasterSurface(10, 10);
        for (var y = 0; y < 10; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                surface.SetPixel(x, y, 255, 0, 0, 255);
            }
        }

        BlurStroke.Apply(surface, [(5f, 5f)], new BrushSettings { Diameter = 8, Hardness = 1 });

        // Interior of the opaque region: blur of opaque stays opaque.
        Assert.Equal(255, surface.GetPixel(0, 0).A);
        Assert.Equal(255, surface.GetPixel(4, 5).A);
        // At the old boundary (was transparent), the blurred sample bleeds in.
        var (_, _, _, a5) = surface.GetPixel(5, 5);
        Assert.InRange(a5, 1, 254);
        var (r6, _, _, a6) = surface.GetPixel(6, 5);
        Assert.InRange(a6, 1, 254);
        Assert.InRange(r6, 1, 255);
    }

    // --- Clone ---

    [Fact]
    public void Clone_CopiesSampleWithOffset()
    {
        var surface = new RasterSurface(20, 20);
        var sample = new RasterSurface(20, 20);
        sample.SetPixel(2, 10, 0, 255, 0, 255); // source pixel

        // Brush at (10,10) with offset dx=-8 => copies sample pixel (2,10).
        CloneStroke.Apply(
            surface, sample.Pixels, [(10f, 10f)], (-8, 0),
            new BrushSettings { Diameter = 4, Hardness = 1 });

        var (r, g, b, a) = surface.GetPixel(10, 10);
        Assert.Equal((0, 255, 0), ((int)r, (int)g, (int)b));
        Assert.Equal(255, a);
    }

    [Fact]
    public void CloneOffset_AlignedKeepsFirst()
    {
        Assert.Equal((-5, 3), CloneStroke.OffsetFor((2, 6), (7, 3), (-5, 3)));
        var recalculated = CloneStroke.OffsetFor((2, 6), (7, 3), null);
        Assert.Equal((-5, 3), recalculated);
    }

    // --- Smudge ---

    [Fact]
    public void SmudgeWeight_SmoothstepProfile()
    {
        Assert.Equal(1f, SmudgeStroke.Weight(0f, 0.5f));
        Assert.Equal(1f, SmudgeStroke.Weight(0.5f, 0.5f));   // at hardness edge
        Assert.Equal(0f, SmudgeStroke.Weight(1f, 0.5f));     // rim
        Assert.True(SmudgeStroke.Weight(0.75f, 0.5f) is > 0f and < 1f);
    }

    [Fact]
    public void Smudge_DragSmearsPixels()
    {
        var surface = new RasterSurface(40, 20);
        // Solid red block on the left half.
        for (var y = 0; y < 20; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                surface.SetPixel(x, y, 255, 0, 0, 255);
            }
        }

        var stroke = new SmudgeStroke(40, 20, new BrushSettings
        {
            Diameter = 8,
            Hardness = 1,
            Opacity = 0.9f,
        }, surface.Pixels);
        stroke.WalkTo(5, 10, surface.Pixels);   // pick up red
        stroke.WalkTo(15, 10, surface.Pixels);  // drag right into transparent area
        stroke.WalkTo(25, 10, surface.Pixels);
        stroke.Commit(surface.Pixels);

        // Somewhere right of the original block there must now be red.
        var foundRed = false;
        for (var x = 10; x < 30; x++)
        {
            var (r, _, _, a) = surface.GetPixel(x, 10);
            if (a > 100 && r > 100)
            {
                foundRed = true;
                break;
            }
        }
        Assert.True(foundRed, "smudge should carry red pixels to the right");
    }
}
