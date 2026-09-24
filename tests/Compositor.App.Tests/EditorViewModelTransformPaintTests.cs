using Compositor.App;
using Compositor.Core;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Once the renderer honours a layer's placement, the brush has to follow it: a stroke
/// started under the cursor must end up under the cursor, not where the layer used to be.
/// Expectations are derived by hand from the placement rect, then checked twice, in the
/// layer's own pixels and in what the canvas actually shows.
/// </summary>
public class EditorViewModelTransformPaintTests
{
    private const int CanvasWidth = 1280;
    private const int CanvasHeight = 720;

    private static EditorViewModel VmMovedBy(int dx)
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        var layer = vm.ActiveLayer!;
        layer.Pixels = new RasterSurface(CanvasWidth, CanvasHeight);
        layer.Transform = new LayerTransform(dx, 0, CanvasWidth, CanvasHeight, 0, false, false);
        vm.Tool = EditorViewModel.EditorTool.Brush;
        return vm;
    }

    private static int AlphaAt(Layer layer, int x, int y)
    {
        var pixels = layer.Pixels!.Pixels;
        return pixels[((y * CanvasWidth) + x) * 4 + 3];
    }

    [Fact]
    public void StrokeOnAMovedLayer_GoesIntoTheLayersOwnSpaceNotTheCanvasSpace()
    {
        // Rect shifted +200: document x=300 is layer x=100, so that is where the ink lives.
        var vm = VmMovedBy(200);
        Assert.True(vm.BeginTool(300, 400));
        vm.EndTool();

        var layer = vm.ActiveLayer!;
        Assert.True(AlphaAt(layer, 100, 400) > 200, "expected the dab at the mapped layer point");
        Assert.Equal(0, AlphaAt(layer, 300, 400));
    }

    [Fact]
    public void StrokeOnAMovedLayer_ShowsUpUnderTheCursorOnceRendered()
    {
        var vm = VmMovedBy(200);
        Assert.True(vm.BeginTool(300, 400));
        vm.EndTool();

        var layer = vm.ActiveLayer!;
        var placed = LayerPlacement.Place(
            layer.Transform, layer.Pixels!.Pixels, CanvasWidth, CanvasHeight, CanvasWidth, CanvasHeight);

        int RenderedAlpha(int x, int y) => placed[((y * CanvasWidth) + x) * 4 + 3];

        Assert.True(RenderedAlpha(300, 400) > 200, "the stroke should appear where the cursor was");
        Assert.Equal(0, RenderedAlpha(100, 400)); // the layer's old position is empty
    }

    [Fact]
    public void StrokeOnAMagnifiedLayer_LandsWhereTheCursorIs()
    {
        // Rect (-1280,-720) size (3840,2160): scale 3 about the canvas centre, so document
        // point (640+100, 360+60) maps to layer point (640+100/3, 360+60/3) = (673,380).
        var vm = new EditorViewModel();
        vm.AddLayer();
        var layer = vm.ActiveLayer!;
        layer.Pixels = new RasterSurface(CanvasWidth, CanvasHeight);
        layer.Transform = new LayerTransform(-1280, -720, 3 * CanvasWidth, 3 * CanvasHeight, 0, false, false);
        vm.Tool = EditorViewModel.EditorTool.Brush;

        Assert.True(vm.BeginTool(740, 420));
        vm.EndTool();

        var pixels = layer.Pixels.Pixels;
        var expectedX = 640 + (740 - 640) / 3.0;
        var expectedY = 360 + (420 - 360) / 3.0;
        var nearestX = (int)Math.Round(expectedX);
        var nearestY = (int)Math.Round(expectedY);
        var alpha = pixels[((nearestY * CanvasWidth) + nearestX) * 4 + 3];
        Assert.True(alpha > 200, $"dab expected near ({expectedX:F1},{expectedY:F1}), alpha {alpha}");

        // And it renders back to the cursor position.
        var placed = LayerPlacement.Place(
            layer.Transform, pixels, CanvasWidth, CanvasHeight, CanvasWidth, CanvasHeight);
        Assert.True(placed[((420 * CanvasWidth) + 740) * 4 + 3] > 200);
    }

    [Fact]
    public void StrokeOnAnUntransformedLayer_StillUsesDocumentCoordinates()
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new RasterSurface(CanvasWidth, CanvasHeight);
        vm.Tool = EditorViewModel.EditorTool.Brush;

        Assert.True(vm.BeginTool(640, 360));
        vm.EndTool();

        Assert.True(AlphaAt(vm.ActiveLayer!, 640, 360) > 200);
    }
}
