using Compositor.Core;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Layer panel behaviors (add / reorder / delete / visibility / selection)
/// on the view-model, bound to a real Core Document.
/// </summary>
public sealed class EditorViewModelTests
{
    [Fact]
    public void Rows_AreTopFirst_NewestOnTop()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("bottom"));
        doc.AddLayer(new Layer("top"));
        var vm = new EditorViewModel(doc);

        Assert.Equal("top", vm.Rows[0].Name);
        Assert.Equal("bottom", vm.Rows[1].Name);
    }

    [Fact]
    public void AddLayer_InsertsAtTop_AndSelectsIt()
    {
        var vm = new EditorViewModel(new Document(100, 100));
        vm.Selected = null;
        var before = vm.Doc.Layers.Count;

        vm.AddLayer();

        Assert.Equal(before + 1, vm.Doc.Layers.Count);
        Assert.Equal(vm.Doc.Layers[^1].Id, vm.Rows[0].Id);
        Assert.NotNull(vm.Selected);
        Assert.Equal(vm.Rows[0].Id, vm.Doc.ActiveLayerId);
    }

    [Fact]
    public void DeleteSelected_RemovesFromDocument_AndSelectsNext()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("bottom"));
        doc.AddLayer(new Layer("top"));
        var vm = new EditorViewModel(doc);

        vm.Selected = vm.Rows[0]; // "top"
        vm.DeleteSelected();

        Assert.Single(doc.Layers);
        Assert.Equal("bottom", doc.Layers[0].Name);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public void MoveSelectedUp_RaisesLayerTowardTopOfStack()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("A")); // bottom
        doc.AddLayer(new Layer("B"));
        doc.AddLayer(new Layer("C")); // top
        var vm = new EditorViewModel(doc);

        vm.Selected = vm.Rows[1]; // B (middle)
        vm.MoveSelectedUp();

        // B swapped past C: doc [A, C, B], UI top-first [B, C, A].
        Assert.Equal("B", vm.Rows[0].Name);
        Assert.Equal("C", vm.Rows[1].Name);
        Assert.Equal("A", vm.Rows[2].Name);
        Assert.Equal(["A", "C", "B"], doc.Layers.Select(l => l.Name));
    }

    [Fact]
    public void MoveSelectedDown_LowersLayerTowardBottomOfStack()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("A")); // bottom
        doc.AddLayer(new Layer("B"));
        doc.AddLayer(new Layer("C")); // top
        var vm = new EditorViewModel(doc);

        vm.Selected = vm.Rows[1]; // B (middle)
        vm.MoveSelectedDown();

        // B swapped past A: doc [B, A, C], UI top-first [C, A, B].
        Assert.Equal(["B", "A", "C"], doc.Layers.Select(l => l.Name));
        Assert.Equal(["C", "A", "B"], vm.Rows.Select(r => r.Name));
    }

    [Fact]
    public void MoveSelected_AtEdges_IsNoOp()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("only"));
        var vm = new EditorViewModel(doc);

        vm.Selected = vm.Rows[0];
        vm.MoveSelectedUp();
        vm.MoveSelectedDown();

        Assert.Single(doc.Layers);
        Assert.Equal("only", doc.Layers[0].Name);
    }

    [Fact]
    public void ToggleVisibility_FlipsUnderlyingLayer()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("a"));
        var vm = new EditorViewModel(doc);
        var row = vm.Rows[0];

        EditorViewModel.ToggleVisibility(row);
        Assert.False(doc.Layers[0].IsVisible);

        EditorViewModel.ToggleVisibility(row);
        Assert.True(doc.Layers[0].IsVisible);
    }

    [Fact]
    public void Selection_UpdatesDocumentActiveLayer()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("a"));
        doc.AddLayer(new Layer("b"));
        var vm = new EditorViewModel(doc);

        vm.Selected = vm.Rows[1]; // "a" (bottom)

        Assert.Equal(doc.Layers[0].Id, vm.Doc.ActiveLayerId);
    }

    [Fact]
    public void VisibilityRowChange_PropagatesToDocument()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("a"));
        var vm = new EditorViewModel(doc);
        var row = vm.Rows[0];

        row.IsVisible = false;

        Assert.False(doc.Layers[0].IsVisible);
    }

    [Fact]
    public void BeginStroke_MaterializesPixels_OnBlankActiveLayer()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("bg"));
        var vm = new EditorViewModel(doc);
        var layer = vm.ActiveLayer!;
        Assert.Null(layer.Pixels);

        Assert.True(vm.BeginStroke(50, 50));

        Assert.NotNull(layer.Pixels);
        Assert.Equal(vm.Doc.Width, layer.Pixels!.Width);
        Assert.True(vm.IsStrokeActive);
    }

    [Fact]
    public void StrokeRoundTrip_UndoRestoresExactBytes_RedoRepaints()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("bg"));
        var vm = new EditorViewModel(doc);
        var layer = vm.ActiveLayer!;
        var before = new byte[100 * 100 * 4]; // blank surface starts all-zero

        Assert.True(vm.BeginStroke(20, 20));
        vm.ContinueStroke(80, 80);
        var painted = (byte[])layer.Pixels!.Pixels.Clone();
        Assert.NotEqual(before, painted);

        Assert.True(vm.EndStroke());
        Assert.True(vm.CanUndo);

        vm.Undo();
        Assert.Equal(before, layer.Pixels!.Pixels);

        vm.Redo();
        Assert.Equal(painted, layer.Pixels!.Pixels);
    }

    [Fact]
    public void SinglePointStroke_ClickDot_UndoRedoRoundTrips()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("bg"));
        var vm = new EditorViewModel(doc);
        var layer = vm.ActiveLayer!;

        Assert.True(vm.BeginStroke(50, 50));
        Assert.True(vm.EndStroke());

        var painted = (byte[])layer.Pixels!.Pixels.Clone();
        Assert.Contains(painted, b => b > 0); // dot landed

        vm.Undo();
        Assert.All(layer.Pixels!.Pixels, b => Assert.Equal(0, b));
        vm.Redo();
        Assert.Equal(painted, layer.Pixels!.Pixels);
    }

    [Fact]
    public void BeginStroke_OnLockedLayer_IsNoOp()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("locked") { IsLocked = true });
        var vm = new EditorViewModel(doc);
        vm.Selected = vm.Rows[0]; // active = locked layer

        Assert.False(vm.BeginStroke(50, 50));
        Assert.Null(vm.ActiveLayer!.Pixels);
        Assert.False(vm.IsStrokeActive);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void BeginStroke_WithoutActiveLayer_IsNoOp()
    {
        var doc = new Document(100, 100); // no layers at all
        var vm = new EditorViewModel(doc);

        Assert.False(vm.BeginStroke(50, 50));
        Assert.False(vm.IsStrokeActive);
    }

    [Fact]
    public void EndStroke_RaisesDocumentChanged_AndFlipsCanUndo()
    {
        var doc = new Document(100, 100);
        doc.AddLayer(new Layer("bg"));
        var vm = new EditorViewModel(doc);
        var changes = new List<string>();
        vm.DocumentChanged += () => changes.Add("changed");
        Assert.False(vm.CanUndo);

        vm.BeginStroke(50, 50);
        Assert.True(vm.EndStroke());

        Assert.NotEmpty(changes);
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);
        Assert.False(vm.IsStrokeActive);

        vm.Undo();
        Assert.True(vm.CanRedo);
    }

    [Fact]
    public void SetBrushColor_UpdatesChannels()
    {
        var vm = new EditorViewModel(new Document(100, 100));

        vm.SetBrushColor(219, 68, 55);

        Assert.Equal((byte)219, vm.BrushR);
        Assert.Equal((byte)68, vm.BrushG);
        Assert.Equal((byte)55, vm.BrushB);
    }
}
