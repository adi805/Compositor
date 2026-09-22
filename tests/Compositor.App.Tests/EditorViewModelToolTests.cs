using Compositor.App;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>Extended tool flows through the view-model: wand, gradient, shape, blur, smudge, clone.</summary>
public class EditorViewModelToolTests
{
    private static EditorViewModel VmWithLayer(int size = 40)
    {
        var vm = new EditorViewModel(new Compositor.Core.Document(size, size));
        vm.AddLayer();
        vm.ActiveLayer!.Pixels = new Compositor.Core.RasterSurface(size, size);
        return vm;
    }

    // --- Magic wand ---

    [Fact]
    public void MagicWand_Click_SelectsContiguousRegion()
    {
        var vm = VmWithLayer();
        // Red square in the middle.
        for (var y = 10; y < 20; y++)
        {
            for (var x = 10; x < 20; x++)
            {
                vm.ActiveLayer!.Pixels!.SetPixel(x, y, 255, 0, 0, 255);
            }
        }

        vm.Tool = EditorViewModel.EditorTool.MagicWand;
        Assert.False(vm.BeginTool(12, 12)); // click, no drag

        Assert.NotNull(vm.Doc.Selection);
        // Selection coverage covers the red square (clipped bounds ~10..20).
        var clip = vm.Doc.Selection!.Clip(40, 40);
        Assert.Equal((10, 10, 10, 10), (clip.X, clip.Y, clip.Width, clip.Height));
    }

    // --- Gradient ---

    [Fact]
    public void GradientDrag_FillsAndUndoes()
    {
        var vm = VmWithLayer();
        vm.Tool = EditorViewModel.EditorTool.Gradient;
        vm.SetBrushColor(255, 0, 0);

        Assert.True(vm.BeginTool(0, 20));
        vm.ContinueTool(40, 20);
        Assert.True(vm.EndTool());

        var (r0, _, _, a0) = vm.ActiveLayer!.Pixels!.GetPixel(0, 20);
        Assert.InRange(r0, 200, 255);
        Assert.InRange(a0, 240, 255); // pixel center t=0.0125 -> ~252

        vm.Undo();
        Assert.Equal(0, vm.ActiveLayer.Pixels.GetPixel(0, 20).A);
    }

    // --- Shape ---

    [Fact]
    public void ShapeDrag_FillsRectAndUndoes()
    {
        var vm = VmWithLayer();
        vm.Tool = EditorViewModel.EditorTool.Shape;
        vm.SetBrushColor(0, 0, 255);

        Assert.True(vm.BeginTool(5, 5));
        vm.ContinueTool(15, 15);
        Assert.True(vm.EndTool());

        var (r, g, b, a) = vm.ActiveLayer!.Pixels!.GetPixel(10, 10);
        Assert.Equal(255, a);
        Assert.Equal(255, b);

        Assert.Equal(0, vm.ActiveLayer.Pixels.GetPixel(2, 2).A);

        vm.Undo();
        Assert.Equal(0, vm.ActiveLayer.Pixels.GetPixel(10, 10).A);
    }

    // --- Blur ---

    [Fact]
    public void BlurStroke_SoftensEdgeIntoTransparent()
    {
        var vm = VmWithLayer(40);
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 20; x++)
            {
                vm.ActiveLayer!.Pixels!.SetPixel(x, y, 255, 0, 0, 255);
            }
        }

        vm.Tool = EditorViewModel.EditorTool.Blur;
        vm.BrushDiameter = 20;
        Assert.True(vm.BeginTool(20, 20));
        Assert.True(vm.EndTool());

        // Right of the edge (was transparent): blurred red bleeds in.
        var (_, _, _, a) = vm.ActiveLayer!.Pixels!.GetPixel(21, 20);
        Assert.InRange(a, 1, 254);
    }

    // --- Smudge ---

    [Fact]
    public void SmudgeDrag_SmearsPaint()
    {
        var vm = VmWithLayer(40);
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 10; x++)
            {
                vm.ActiveLayer!.Pixels!.SetPixel(x, y, 255, 0, 0, 255);
            }
        }

        vm.Tool = EditorViewModel.EditorTool.Smudge;
        vm.BrushDiameter = 10;
        vm.BrushOpacity = 0.9f;
        Assert.True(vm.BeginTool(5, 20));
        vm.ContinueTool(15, 20);
        vm.ContinueTool(25, 20);
        Assert.True(vm.EndTool());

        var found = false;
        for (var x = 10; x < 30; x++)
        {
            var (r, _, _, a) = vm.ActiveLayer!.Pixels!.GetPixel(x, 20);
            if (a > 50 && r > 50)
            {
                found = true;
                break;
            }
        }
        Assert.True(found, "smudge should carry red to the right");
    }

    // --- Clone stamp ---

    [Fact]
    public void CloneStamp_RequiresSource()
    {
        var vm = VmWithLayer();
        vm.Tool = EditorViewModel.EditorTool.CloneStamp;
        Assert.False(vm.BeginTool(10, 10)); // no source yet
        Assert.False(vm.IsStrokeActive);
    }

    [Fact]
    public void CloneStamp_CopiesFromSource()
    {
        var vm = VmWithLayer();
        vm.ActiveLayer!.Pixels!.SetPixel(2, 20, 0, 255, 0, 255); // source pixel

        vm.Tool = EditorViewModel.EditorTool.CloneStamp;
        vm.SetCloneSource(2, 20);
        vm.BrushDiameter = 4;
        vm.BrushHardness = 1;

        // Stroke at (20,20): offset = source - first point = (-18, 0).
        Assert.True(vm.BeginTool(20, 20));
        Assert.True(vm.EndTool());

        var (r, g, b, a) = vm.ActiveLayer!.Pixels!.GetPixel(20, 20);
        Assert.Equal(255, a);
        Assert.Equal(255, g); // copied the green source pixel
        Assert.Equal(0, r);
    }
}
