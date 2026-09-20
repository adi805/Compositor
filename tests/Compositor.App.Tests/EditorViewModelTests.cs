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
}
