using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Flatten tests use hand-computed straight-alpha over-compositing values.
/// Example: 50% red over opaque green with a_out = 1 gives
/// r = 255*0.502 = 128, g = 255*0.498 = 127 exactly.
/// </summary>
public sealed class FlattenTests
{
    private static RasterSurface Solid(int w, int h, byte r, byte g, byte b, byte a)
    {
        var surface = new RasterSurface(w, h);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                surface.SetPixel(x, y, r, g, b, a);
            }
        }

        return surface;
    }

    [Fact]
    public void Flatten_NoLayers_ProducesTransparent()
    {
        var doc = new Document(4, 3);
        var (w, h, rgba) = Flatten.ToRgba(doc);
        Assert.Equal(4, w);
        Assert.Equal(3, h);
        Assert.All(rgba, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Flatten_SingleOpaqueLayer_PassesThrough()
    {
        var doc = new Document(2, 2);
        doc.AddLayer(new Layer("L") { Pixels = Solid(2, 2, 255, 0, 0, 255) });
        var (_, _, rgba) = Flatten.ToRgba(doc);
        Assert.Equal(255, rgba[0]);
        Assert.Equal(0, rgba[1]);
        Assert.Equal(0, rgba[2]);
        Assert.Equal(255, rgba[3]);
    }

    [Fact]
    public void Flatten_SemiTransparentTop_OverComposites()
    {
        // 128/255 red (a=128) over opaque green: out = (128, 127, 0, 255).
        var doc = new Document(1, 1);
        doc.AddLayer(new Layer("bottom") { Pixels = Solid(1, 1, 0, 255, 0, 255) });
        doc.AddLayer(new Layer("top") { Pixels = Solid(1, 1, 255, 0, 0, 128) });
        var (_, _, rgba) = Flatten.ToRgba(doc);
        Assert.Equal(128, rgba[0]);
        Assert.Equal(127, rgba[1]);
        Assert.Equal(0, rgba[2]);
        Assert.Equal(255, rgba[3]);
    }

    [Fact]
    public void Flatten_HiddenOrBlankOrZeroOpacity_Skipped()
    {
        var doc = new Document(1, 1);
        doc.AddLayer(new Layer("hidden") { Pixels = Solid(1, 1, 255, 0, 0, 255), IsVisible = false });
        doc.AddLayer(new Layer("blank")); // Pixels null
        doc.AddLayer(new Layer("transparent") { Pixels = Solid(1, 1, 255, 0, 0, 255), Opacity = 0.0 });
        var (_, _, rgba) = Flatten.ToRgba(doc);
        Assert.All(rgba, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Flatten_HalfOpacity_OpensAlphaCorrectly()
    {
        // Single layer at 50% opacity over nothing: a = 255*0.5 = 127.5 -> 128,
        // and un-premultiplying keeps the color channel intact.
        var doc = new Document(1, 1);
        doc.AddLayer(new Layer("l") { Pixels = Solid(1, 1, 200, 100, 50, 255), Opacity = 0.5 });
        var (_, _, rgba) = Flatten.ToRgba(doc);
        Assert.Equal(200, rgba[0]);
        Assert.Equal(100, rgba[1]);
        Assert.Equal(50, rgba[2]);
        Assert.Equal(128, rgba[3]);
    }

    [Fact]
    public void ToRgba_PlacesALayerSmallerThanTheCanvas()
    {
        // A layer whose surface is smaller than the canvas is drawn on the canvas, so it must appear in the
        // export too. This used to be skipped outright ("full-canvas surfaces only"), which made an imported
        // image visible in the window and absent from the saved file.
        var doc = new Document(64, 64);
        var layer = new Layer("Small");
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 3] = 255;
        }

        layer.Pixels = new RasterSurface(16, 16, pixels);
        layer.Transform = new LayerTransform(20, 10, 16, 16, 0, false, false);
        doc.AddLayer(layer);

        var (width, height, rgba) = Flatten.ToRgba(doc);

        Assert.Equal(64, width);
        Assert.Equal(64, height);
        Assert.Equal(255, rgba[((15 * width) + 25) * 4 + 3]);      // inside the placement rect
        Assert.Equal(255, rgba[((10 * width) + 20) * 4 + 3]);      // its top-left corner
        Assert.Equal(0, rgba[((9 * width) + 25) * 4 + 3]);         // one row above it
        Assert.Equal(0, rgba[((15 * width) + 19) * 4 + 3]);        // one column left of it
    }
}
