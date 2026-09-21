using Compositor.Core;
using Compositor.Core.Selection;
using Compositor.App;
using Xunit;

namespace Compositor.App.Tests;

public class EditorViewModelSelectionTests
{
    private static EditorViewModel VmWithLayerAndPixels()
    {
        var vm = new EditorViewModel();
        vm.AddLayer();
        vm.AddLayer(); // second layer becomes active
        vm.ActiveLayer!.Pixels = new RasterSurface(vm.Doc.Width, vm.Doc.Height);
        return vm;
    }

    [Fact]
    public void MarqueeDrag_CreatesSelection()
    {
        var vm = VmWithLayerAndPixels();
        vm.Tool = EditorViewModel.EditorTool.RectangleSelect;

        Assert.True(vm.BeginMarquee(2, 2));
        vm.ContinueMarquee(6, 5);
        Assert.NotNull(vm.DraftBounds);
        vm.EndMarquee();

        Assert.Null(vm.DraftBounds);
        Assert.False(vm.IsMarqueeActive);
        Assert.NotNull(vm.Doc.Selection);
        var clip = vm.Doc.Selection!.Clip(vm.Doc.Width, vm.Doc.Height);
        Assert.Equal(2, clip.X);
        Assert.Equal(2, clip.Y);
    }

    [Fact]
    public void MarqueeIgnoresBrushTool()
    {
        var vm = VmWithLayerAndPixels();
        vm.Tool = EditorViewModel.EditorTool.Brush;
        Assert.False(vm.BeginMarquee(0, 0));
    }

    [Fact]
    public void PaintThroughSelection_ConstrainsAndUndoRestores()
    {
        var vm = VmWithLayerAndPixels();
        vm.Tool = EditorViewModel.EditorTool.RectangleSelect;
        vm.BeginMarquee(0, 0);
        vm.ContinueMarquee(4, 4);
        vm.EndMarquee();

        vm.Tool = EditorViewModel.EditorTool.Brush;
        Assert.True(vm.BeginStroke(2, 2));
        vm.ContinueStroke(8, 2); // far beyond the selection
        vm.EndStroke();

        var pixels = vm.ActiveLayer!.Pixels!.Pixels;
        var inside = pixels[((2 * vm.Doc.Width) + 2) * 4 + 3];
        var outside = pixels[((2 * vm.Doc.Width) + 7) * 4 + 3];
        Assert.True(inside > 0, "pixel inside selection should be painted");
        Assert.Equal(0, outside);

        vm.Undo();
        Assert.Equal(0, pixels[((2 * vm.Doc.Width) + 2) * 4 + 3]);
    }

    [Fact]
    public void CutCopyPaste_FloatingCommitRoundTrip()
    {
        var vm = VmWithLayerAndPixels();
        var layer = vm.ActiveLayer!;
        layer.Pixels!.SetPixel(1, 1, 255, 0, 0, 255);
        layer.Pixels!.SetPixel(2, 1, 255, 0, 0, 255);

        vm.Doc.Selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(1, 1, 2, 1, SelectionMode.Replace));

        vm.CopySelection();
        vm.CutSelection();
        Assert.Equal((0, 0, 0, 0), layer.Pixels!.GetPixel(1, 1));

        vm.PasteSelection();
        Assert.NotNull(vm.Floating);
        vm.MoveFloating(3, 3);
        vm.CommitFloating();

        Assert.Null(vm.Floating);
        Assert.Equal((255, 0, 0, 255), layer.Pixels!.GetPixel(4, 4));

        vm.Undo();
        Assert.Equal((0, 0, 0, 0), layer.Pixels!.GetPixel(4, 4));
    }

    [Fact]
    public void SelectAllThenInvert_ClearsSelection()
    {
        var vm = VmWithLayerAndPixels();
        vm.SelectAll();
        Assert.NotNull(vm.Doc.Selection);
        vm.InvertSelection();
        Assert.Null(vm.Doc.Selection); // inverted-all = empty = stored as none
    }
}
