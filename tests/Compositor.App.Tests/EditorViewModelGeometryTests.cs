using Compositor.Core;
using Compositor.Core.Selection;
using Compositor.App;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>Geometry flows on the view-model: canvas size / image size / crop.</summary>
public class EditorViewModelGeometryTests
{
    private static EditorViewModel VmWithPixelLayer(int docW = 8, int docH = 8)
    {
        var vm = new EditorViewModel(new Document(docW, docH));
        vm.AddLayer();
        var surface = new RasterSurface(docW, docH);
        for (var i = 3; i < surface.Pixels.Length; i += 4)
        {
            surface.Pixels[i] = 255;
        }
        vm.ActiveLayer!.Pixels = surface;
        return vm;
    }

    [Fact]
    public void ApplyCanvasSize_ChangesDims_ShiftsOrigins_Undoable()
    {
        var vm = VmWithPixelLayer();
        var layer = vm.ActiveLayer!;
        var before = (byte[])layer.Pixels!.Pixels.Clone();

        Assert.True(vm.ApplyCanvasSize(12, 10, 4, null));

        Assert.Equal((12, 10), (vm.Doc.Width, vm.Doc.Height));
        Assert.Equal(2.0, layer.Transform.OriginX); // center anchor: floor(4*1/2)
        Assert.Equal(before, layer.Pixels!.Pixels); // non-destructive
        Assert.True(vm.CanUndo);

        vm.Undo();
        Assert.Equal((8, 8), (vm.Doc.Width, vm.Doc.Height));
        Assert.Equal(0.0, layer.Transform.OriginX);
    }

    [Fact]
    public void ApplyCanvasSize_WithFill_InsertsBottomExtensionLayer()
    {
        var vm = VmWithPixelLayer();

        Assert.True(vm.ApplyCanvasSize(16, 16, 0, (255, 255, 255)));

        Assert.Equal(2, vm.Doc.Layers.Count);
        var fill = vm.Doc.Layers[0];
        Assert.Equal("Canvas Extension", fill.Name);
        Assert.Equal((byte)0, fill.Pixels!.GetPixel(0, 0).A);      // hole over old canvas
        Assert.Equal((byte)255, fill.Pixels.GetPixel(15, 15).A);   // fill outside

        vm.Undo();
        Assert.Single(vm.Doc.Layers);
    }

    [Fact]
    public void ApplyCanvasSize_RejectsInvalid()
    {
        var vm = VmWithPixelLayer();
        Assert.False(vm.ApplyCanvasSize(0, 8, 4, null));
        Assert.False(vm.ApplyCanvasSize(8, 8, 9, null));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void ApplyImageSize_ResamplesPixels_UpdatesResolution_Undoable()
    {
        var vm = VmWithPixelLayer(4, 4);

        Assert.True(vm.ApplyImageSize(8, 8, 300));

        Assert.Equal((8, 8), (vm.Doc.Width, vm.Doc.Height));
        Assert.Equal(300, vm.Doc.Resolution);
        var pixels = vm.ActiveLayer!.Pixels!;
        Assert.Equal((8, 8), (pixels.Width, pixels.Height));

        vm.Undo();
        Assert.Equal((4, 4), (vm.Doc.Width, vm.Doc.Height));
        Assert.Equal((4, 4), (vm.ActiveLayer!.Pixels!.Width, vm.ActiveLayer.Pixels.Height));
    }

    [Fact]
    public void ApplyImageSize_RejectsInvalidResolution()
    {
        var vm = VmWithPixelLayer();
        Assert.False(vm.ApplyImageSize(8, 8, 0));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void ApplyCrop_ShrinksCanvas_AndShiftsContent()
    {
        var vm = VmWithPixelLayer(16, 16);
        var layer = vm.ActiveLayer!;

        Assert.True(vm.ApplyCrop(4, 6, 8, 8));

        Assert.Equal((8, 8), (vm.Doc.Width, vm.Doc.Height));
        Assert.Equal(-4.0, layer.Transform.OriginX);
        Assert.Equal(-6.0, layer.Transform.OriginY);

        vm.Undo();
        Assert.Equal((16, 16), (vm.Doc.Width, vm.Doc.Height));
    }

    [Fact]
    public void ApplyCrop_RejectsInvalid()
    {
        var vm = VmWithPixelLayer();
        Assert.False(vm.ApplyCrop(0, 0, 0, 8));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void SelectionPixelBounds_ReturnsShapeUnion()
    {
        var vm = VmWithPixelLayer(32, 32);
        vm.Doc.Selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(5, 7, 10, 4, SelectionMode.Replace));

        var bounds = vm.SelectionPixelBounds();

        Assert.Equal((5, 7, 10, 4), bounds);
    }

    [Fact]
    public void SelectionPixelBounds_NullWithoutSelection()
    {
        var vm = VmWithPixelLayer();
        Assert.Null(vm.SelectionPixelBounds());
    }

    [Fact]
    public void ApplyCropToSelection_UsesSelectionBounds()
    {
        var vm = VmWithPixelLayer(32, 32);
        vm.Doc.Selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(8, 8, 16, 16, SelectionMode.Replace));

        Assert.True(vm.ApplyCropToSelection());

        Assert.Equal((16, 16), (vm.Doc.Width, vm.Doc.Height));
    }

    [Fact]
    public void ApplyCropToSelection_FalseWithoutSelection()
    {
        var vm = VmWithPixelLayer();
        Assert.False(vm.ApplyCropToSelection());
        Assert.Equal((8, 8), (vm.Doc.Width, vm.Doc.Height));
    }
}
