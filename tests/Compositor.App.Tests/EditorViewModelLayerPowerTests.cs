using Compositor.Core;
using Compositor.Core.Commands;
using Compositor.Core.Imaging;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Layer power flows on the view-model (parity WS5): groups, merge down,
/// flip, appearance editing, and undo across each.
/// </summary>
public sealed class EditorViewModelLayerPowerTests
{
    private static Layer PixelLayer(string name, int w = 8, int h = 8, byte alpha = 255)
    {
        var surface = new RasterSurface(w, h);
        for (var i = 3; i < surface.Pixels.Length; i += 4)
        {
            surface.Pixels[i] = alpha;
        }
        surface.MarkDirty();
        return new Layer(name) { Pixels = surface };
    }

    private static (EditorViewModel Vm, Layer Bottom, Layer Top) Setup()
    {
        var doc = new Document(8, 8);
        var bottom = PixelLayer("bottom");
        var top = PixelLayer("top");
        doc.Layers.AddRange([bottom, top]);
        doc.ActiveLayerId = top.Id;
        return (new EditorViewModel(doc), bottom, top);
    }

    [Fact]
    public void GroupSelected_WrapsActiveLayer_AndUndoRestores()
    {
        var (vm, bottom, top) = Setup();
        vm.Selected = vm.Rows.First(r => r.Id == top.Id);

        vm.GroupSelected();

        Assert.Equal(3, vm.Doc.Layers.Count);
        var group = vm.Doc.Layers[1];
        Assert.True(group.IsGroup);
        Assert.Equal("Folder 1", group.Name);
        Assert.Equal(group.Id, top.ParentId);
        Assert.False(vm.Doc.Layers[0].IsGroup); // bottom untouched at root

        vm.Undo();
        Assert.Equal(2, vm.Doc.Layers.Count);
        Assert.Null(top.ParentId);
    }

    [Fact]
    public void AddGroup_InsertsAboveActive_AndSelectable()
    {
        var (vm, _, top) = Setup();

        vm.AddGroup();

        var group = Assert.Single(vm.Doc.Layers, l => l.IsGroup);
        Assert.Equal(2, vm.Doc.Layers.IndexOf(group)); // after top = above it in the stack
        Assert.NotNull(vm.Selected);
        Assert.Equal(group.Id, vm.Selected!.Id);
        Assert.False(vm.CanEditAppearance); // groups are not appearance-editable
    }

    [Fact]
    public void MergeDown_BakesTopIntoBottom_AndUndoRestoresBoth()
    {
        var (vm, bottom, top) = Setup();

        vm.MergeDown();

        var merged = Assert.Single(vm.Doc.Layers);
        Assert.Equal("bottom", merged.Name);
        Assert.NotNull(merged.Pixels);
        vm.Undo();
        Assert.Equal(2, vm.Doc.Layers.Count);
        Assert.Same(bottom, vm.Doc.Layers[0]);
        Assert.Same(top, vm.Doc.Layers[1]);
    }

    [Fact]
    public void MergeDown_DoesNothingWithoutPixelLayerBelow()
    {
        var doc = new Document(8, 8);
        var only = PixelLayer("only");
        doc.AddLayer(only);
        doc.ActiveLayerId = only.Id;
        var vm = new EditorViewModel(doc);

        vm.MergeDown();

        Assert.Single(vm.Doc.Layers);
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void FlipActive_MirrorsPixels_AndUndoRestores()
    {
        var (vm, _, top) = Setup();
        top.Pixels!.Clear();
        top.Pixels.SetPixel(0, 0, 255, 0, 0, 255);
        top.Pixels.MarkDirty();
        top.Transform = new LayerTransform(0, 0, 8, 8, 0, false, false);

        vm.FlipActive(horizontally: true);

        Assert.Equal((byte)255, top.Pixels.GetPixel(7, 0).R);
        Assert.True(top.Transform.FlipH);

        vm.Undo();
        Assert.Equal((byte)255, top.Pixels.GetPixel(0, 0).R);
        Assert.False(top.Transform.FlipH);
    }

    [Fact]
    public void FlipCanvas_MirrorsEverything_AndUndoRestores()
    {
        var (vm, bottom, top) = Setup();
        bottom.Pixels!.Clear();
        bottom.Pixels.SetPixel(0, 0, 255, 0, 0, 255);
        vm.FlipCanvas(horizontally: true);

        Assert.Equal((byte)255, bottom.Pixels.GetPixel(7, 0).R);
        Assert.True(bottom.Transform.FlipH);
        Assert.True(top.Transform.FlipH);

        vm.Undo();
        Assert.Equal((byte)255, bottom.Pixels.GetPixel(0, 0).R);
        Assert.False(bottom.Transform.FlipH);
    }

    [Fact]
    public void ActiveOpacity_And_Blend_PushUndoableCommands()
    {
        var (vm, _, top) = Setup();
        vm.Selected = vm.Rows.First(r => r.Id == top.Id);

        vm.ActiveOpacity = 0.25;
        Assert.Equal(0.25, top.Opacity);
        vm.ActiveBlend = BlendMode.Multiply;
        Assert.Equal(BlendMode.Multiply, top.Blend);

        vm.Undo(); // blend back
        Assert.Equal(BlendMode.Normal, top.Blend);
        vm.Undo(); // opacity back
        Assert.Equal(1.0, top.Opacity);
    }

    [Fact]
    public void ActiveOpacity_Clamps_AndIgnoresSameValue()
    {
        var (vm, _, top) = Setup();
        vm.Selected = vm.Rows.First(r => r.Id == top.Id);

        vm.ActiveOpacity = 5.0;
        Assert.Equal(1.0, top.Opacity); // clamped, no-op, no command
        Assert.False(vm.History.CanUndo);

        vm.ActiveOpacity = 0.5;
        vm.ActiveOpacity = 0.5; // same value: no extra command
        Assert.True(vm.History.CanUndo);
    }

    [Fact]
    public void ActiveScale_SetsTransform_KeepingCenter()
    {
        var (vm, _, top) = Setup();
        vm.Selected = vm.Rows.First(r => r.Id == top.Id);
        top.Transform = new LayerTransform(0, 0, 8, 8, 0, false, false);

        vm.ActiveScalePercent = 50;

        Assert.Equal(4, top.Transform.Width);
        Assert.Equal(4, top.Transform.Height);
        Assert.Equal(2, top.Transform.OriginX); // 8*50%=4 wide, center kept at 4 → origin 2
        Assert.Equal(4, top.Transform.CenterX);
        vm.Undo();
        Assert.Equal(8, top.Transform.Width);
    }

    [Fact]
    public void HierarchyRows_IndentDepth_AndHiddenGroupHidesCanvas()
    {
        var doc = new Document(8, 8);
        var bottom = PixelLayer("bottom");
        var group = Layer.Group("Folder 1");
        var child = PixelLayer("child");
        child.ParentId = group.Id;
        doc.Layers.AddRange([bottom, group, child]);

        var vm = new EditorViewModel(doc);

        var rows = vm.Rows;
        Assert.Equal(3, rows.Count);
        // Top-first: child (depth 1) above its group (depth 0), then bottom (depth 0).
        Assert.Equal(child.Id, rows[0].Id);
        Assert.Equal(1, rows[0].Depth);
        Assert.Equal(group.Id, rows[1].Id);
        Assert.True(rows[1].IsGroup);
        Assert.Equal(bottom.Id, rows[2].Id);
        Assert.Equal(0, rows[2].Depth);

        // A hidden group hides its subtree in the composited output.
        group.IsVisible = false;
        bottom.IsVisible = false;
        var (_, _, rgba) = Flatten.ToRgba(doc);
        Assert.All(range(rgba.Length), i => Assert.Equal(0, rgba[i]));

        static IEnumerable<int> range(int n)
        {
            for (var i = 0; i < n; i++)
            {
                yield return i;
            }
        }
    }
}
