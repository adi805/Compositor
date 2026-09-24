using Compositor.App;
using Compositor.Core;
using Compositor.Core.Adjustments;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// The two places that used to read canvas coordinates on a transformed layer: the magic wand, and the clip an
/// adjustment or filter sheet is given. Both work on the layer's own pixel buffer, so both have to go through
/// the same mapping the brush uses. Expected values are derived by hand from the placement mapping.
/// </summary>
public class LayerSpaceEditingTests
{
    // A 100x100 layer placed at document (200,100) at 1:1: layer (lx,ly) sits at document (200+lx, 100+ly).
    private const int LayerOriginX = 200;
    private const int LayerOriginY = 100;
    private const int LayerSide = 100;

    /// <summary>
    /// Document, one 100x100 layer placed at (200,100), move tool irrelevant. The layer is left half black,
    /// right half white, which gives the wand a clean edge to find at layer x = 50.
    /// </summary>
    private static (EditorViewModel Vm, RasterSurface Surface) SplitLayer()
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        var surface = new RasterSurface(LayerSide, LayerSide);
        for (var y = 0; y < LayerSide; y++)
        {
            for (var x = 0; x < LayerSide; x++)
            {
                var value = x < LayerSide / 2 ? (byte)0 : (byte)255;
                surface.SetPixel(x, y, value, value, value, 255);
            }
        }

        vm.ActiveLayer!.Pixels = surface;
        vm.ActiveLayer!.Transform = new LayerTransform(LayerOriginX, LayerOriginY, LayerSide, LayerSide, 0, false, false);
        return (vm, surface);
    }

    private static byte CoverageAt(EditorViewModel vm, int x, int y)
    {
        var coverage = vm.Doc.Selection!.RenderCoverage(vm.Doc.Width, vm.Doc.Height);
        return coverage[(y * vm.Doc.Width) + x];
    }

    [Fact]
    public void Wand_OnATranslatedLayer_SelectsThePixelsUnderTheCursor()
    {
        var (vm, _) = SplitLayer();
        vm.Tool = EditorViewModel.EditorTool.MagicWand;
        vm.WandTolerance = 0;

        // Document (220,120) is layer (20,20), which is in the black half. The wand should pick that half, and
        // the result has to come back out on the canvas where the layer is drawn.
        Assert.False(vm.BeginTool(220, 120));

        Assert.NotNull(vm.Doc.Selection);
        Assert.True(CoverageAt(vm, 220, 120) > 0, "the clicked point must be selected");
        Assert.True(CoverageAt(vm, 210, 110) > 0, "the whole black half must be selected");
        Assert.Equal(0, CoverageAt(vm, 280, 120));   // layer x 80: the white half
        Assert.Equal(0, CoverageAt(vm, 100, 120));   // left of the layer: nothing was ever there
        Assert.Equal(0, CoverageAt(vm, 400, 120));   // right of the layer
    }

    [Fact]
    public void Wand_SelectsOnlyTheLayersOwnFootprint()
    {
        var (vm, _) = SplitLayer();
        vm.Tool = EditorViewModel.EditorTool.MagicWand;
        vm.WandTolerance = 255;   // match everything, so the only limit is the layer's own extent

        Assert.False(vm.BeginTool(220, 120));

        // The layer covers document x 200..299 and y 100..199, and nothing outside it may be selected.
        Assert.True(CoverageAt(vm, 200, 100) > 0);
        Assert.True(CoverageAt(vm, 299, 199) > 0);
        Assert.Equal(0, CoverageAt(vm, 199, 150));
        Assert.Equal(0, CoverageAt(vm, 300, 150));
        Assert.Equal(0, CoverageAt(vm, 250, 99));
        Assert.Equal(0, CoverageAt(vm, 250, 200));
    }

    [Fact]
    public void Wand_OnAnUntransformedLayer_BehavesExactlyAsBefore()
    {
        // The identity path must stay untouched: same surface size as the canvas, no mapping.
        var vm = new EditorViewModel();
        vm.AddLayer();
        var surface = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        for (var y = 0; y < vm.Doc.Height; y++)
        {
            for (var x = 0; x < vm.Doc.Width; x++)
            {
                var value = x < vm.Doc.Width / 2 ? (byte)0 : (byte)255;
                surface.SetPixel(x, y, value, value, value, 255);
            }
        }

        vm.ActiveLayer!.Pixels = surface;
        vm.Tool = EditorViewModel.EditorTool.MagicWand;
        vm.WandTolerance = 0;

        Assert.False(vm.BeginTool(10, 10));

        Assert.True(CoverageAt(vm, 10, 10) > 0);
        Assert.Equal(0, CoverageAt(vm, vm.Doc.Width - 10, 10));
    }

    [Fact]
    public void AdjustmentClip_OnATranslatedLayer_IsTakenInLayerSpace()
    {
        var (vm, _) = SplitLayer();
        vm.Tool = EditorViewModel.EditorTool.RectangleSelect;

        // Select document (200,100) to (250,150): the top-left quadrant of the placed layer, which is layer
        // (0,0) to (50,50).
        Assert.True(vm.BeginMarquee(200, 100));
        vm.ContinueMarquee(250, 150);
        vm.EndMarquee();

        var clip = vm.CurrentAdjustmentClip;

        // The clip covers the whole layer surface and carries the selection in its coverage: a rotated or
        // scaled selection has no tight bounding rect in layer space, so the shape lives in the mask.
        Assert.NotNull(clip);
        Assert.Equal(0, clip!.X);
        Assert.Equal(0, clip.Y);
        Assert.Equal(LayerSide, clip.Width);
        Assert.Equal(LayerSide, clip.Height);

        // The selected quadrant is layer (0,0)..(50,50), so the inside is fully covered and the outside is not.
        Assert.Equal(1f, clip.FactorAt(10, 10));
        Assert.Equal(1f, clip.FactorAt(49, 49));
        Assert.Equal(0f, clip.FactorAt(60, 10));
        Assert.Equal(0f, clip.FactorAt(10, 60));
    }

    [Fact]
    public void AdjustmentClip_ThenInvert_LandsInsideTheSelectionAsRendered()
    {
        // The end-to-end claim: an effect driven by the sheet's clip must change the pixels the user selected,
        // in the place the user sees them. Layer is all black; the selection is the layer's top-left quadrant.
        var vm = new EditorViewModel();
        vm.AddLayer();
        var surface = new RasterSurface(LayerSide, LayerSide);
        for (var y = 0; y < LayerSide; y++)
        {
            for (var x = 0; x < LayerSide; x++)
            {
                surface.SetPixel(x, y, 0, 0, 0, 255);
            }
        }

        vm.ActiveLayer!.Pixels = surface;
        vm.ActiveLayer!.Transform = new LayerTransform(LayerOriginX, LayerOriginY, LayerSide, LayerSide, 0, false, false);
        vm.Tool = EditorViewModel.EditorTool.RectangleSelect;

        Assert.True(vm.BeginMarquee(200, 100));
        vm.ContinueMarquee(250, 150);
        vm.EndMarquee();

        var inverted = AdjustmentRunner.ApplyInvert(surface, vm.CurrentAdjustmentClip);
        vm.ActiveLayer!.Pixels = inverted;

        var (_, _, rgba) = Flatten.ToRgba(vm.Doc);

        // Document (220,120) is inside the selection, so it inverted to white. (280,120) is outside it, so it
        // stayed black. Without the layer-space clip the first would not have changed at all.
        Assert.Equal(255, Red(rgba, vm.Doc.Width, 220, 120));
        Assert.Equal(0, Red(rgba, vm.Doc.Width, 280, 120));
    }

    [Fact]
    public void AdjustmentClip_WithNoSelection_IsNull()
    {
        var (vm, _) = SplitLayer();

        Assert.Null(vm.CurrentAdjustmentClip);
    }

    private static byte Red(byte[] rgba, int width, int x, int y) => rgba[((y * width) + x) * 4];
}
