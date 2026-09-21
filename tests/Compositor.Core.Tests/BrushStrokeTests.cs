using Compositor.Core;
using Compositor.Core.Project;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>Brush paints pixels; undo/redo restore them exactly.</summary>
public sealed class BrushStrokeTests
{
    private static List<(float X, float Y)> HorizontalPath(int y, int x0, int x1) =>
        Enumerable.Range(x0, x1 - x0 + 1).Select(x => ((float)x, (float)y)).ToList();

    [Fact]
    public void Stroke_ChangesPixelsInsideRadius()
    {
        var surface = new RasterSurface(100, 100);
        var path = HorizontalPath(50, 10, 90);

        BrushStroke.Apply(surface, path, radius: 5, 255, 0, 0, 255, opacity: 1f);

        var (r, g, b, a) = surface.GetPixel(50, 50);
        Assert.Equal(255, r);
        Assert.Equal(0, g);
        Assert.Equal(255, a);
    }

    [Fact]
    public void Stroke_DoesNotPaintOutsidePath()
    {
        var surface = new RasterSurface(100, 100);
        var path = HorizontalPath(50, 10, 90);

        BrushStroke.Apply(surface, path, radius: 5, 255, 0, 0, 255, 1f);

        var (r, g, b, a) = surface.GetPixel(50, 5); // 45px above the path
        Assert.Equal(0, a);
        Assert.Equal((0, 0, 0), (r, g, b));
    }

    [Fact]
    public void Stroke_OpacityZero_PaintsNothing()
    {
        var surface = new RasterSurface(50, 50);
        var path = HorizontalPath(25, 5, 45);

        BrushStroke.Apply(surface, path, radius: 5, 255, 255, 255, 255, opacity: 0f);

        Assert.Equal(0, surface.GetPixel(25, 25).A);
    }

    [Fact]
    public void Stroke_HalfOpacity_ProducesHalfAlpha()
    {
        var surface = new RasterSurface(50, 50);

        BrushStroke.Apply(
            surface, [(25f, 25f)], radius: 10, 255, 0, 0, 255, opacity: 0.5f);

        var (r, g, b, a) = surface.GetPixel(25, 25); // center: coverage saturates to full
        Assert.InRange(a, 126, 130); // opacity cap is exact: 255 * 0.5 = 127.5 -> 128
        Assert.InRange(r, 240, 255);
    }

    [Fact]
    public void EmptyPath_IsNoOp()
    {
        var surface = new RasterSurface(10, 10);
        BrushStroke.Apply(surface, [], 5, 255, 0, 0, 255, 1f);
        Assert.All(surface.Pixels, px => Assert.Equal(0, px));
    }

    [Fact]
    public void PaintUndoRedo_RestoresAndReappliesExactly()
    {
        var surface = new RasterSurface(100, 100);
        var history = new UndoHistory();
        var path = HorizontalPath(50, 10, 90);
        var before = (byte[])surface.Pixels.Clone();

        history.Push(new PaintCommand(surface, path, 5, 0, 255, 0, 255, 1f));
        Assert.InRange(surface.GetPixel(50, 50).A, 240, 255); // painted

        history.Undo();
        Assert.Equal(before, surface.Pixels); // exact revert

        history.Redo();
        Assert.InRange(surface.GetPixel(50, 50).A, 240, 255); // repainted

        history.Undo();
        Assert.Equal(before, surface.Pixels);
    }

    [Fact]
    public void TwoStrokes_TwoUndos()
    {
        var surface = new RasterSurface(100, 100);
        var history = new UndoHistory();
        var original = (byte[])surface.Pixels.Clone();

        history.Push(new PaintCommand(surface, HorizontalPath(30, 10, 90), 4, 255, 0, 0, 255, 1f));
        history.Push(new PaintCommand(surface, HorizontalPath(70, 10, 90), 4, 0, 0, 255, 255, 1f));
        Assert.InRange(surface.GetPixel(30, 30).A, 240, 255);
        Assert.InRange(surface.GetPixel(70, 70).A, 240, 255);

        history.Undo();
        Assert.Equal(0, surface.GetPixel(70, 70).A);
        Assert.InRange(surface.GetPixel(30, 30).A, 240, 255);

        history.Undo();
        Assert.Equal(original, surface.Pixels);
    }
}
