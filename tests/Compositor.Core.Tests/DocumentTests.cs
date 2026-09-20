using Compositor.Core;
using Compositor.Core.Commands;
using Xunit;

namespace Compositor.Core.Tests;

public class DocumentTests
{
    [Fact]
    public void Constructor_RejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Document(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Document(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Document(-5, 100));
    }

    [Fact]
    public void Constructor_AcceptsValidDimensions()
    {
        var doc = new Document(1920, 1080);
        Assert.Equal(1920, doc.Width);
        Assert.Equal(1080, doc.Height);
        Assert.Empty(doc.Layers);
    }

    [Fact]
    public void AddLayer_AppendsInOrder()
    {
        var doc = new Document(100, 100);
        var a = new Layer("Background");
        var b = new Layer("Foreground");
        doc.AddLayer(a);
        doc.AddLayer(b);
        Assert.Equal(2, doc.Layers.Count);
        Assert.Same(a, doc.Layers[0]);
        Assert.Same(b, doc.Layers[1]);
    }

    [Fact]
    public void RemoveLayer_RemovesCorrectLayer()
    {
        var doc = new Document(100, 100);
        var a = new Layer("A");
        var b = new Layer("B");
        doc.AddLayer(a);
        doc.AddLayer(b);
        Assert.True(doc.RemoveLayer(a));
        Assert.Single(doc.Layers);
        Assert.Same(b, doc.Layers[0]);
    }

    [Fact]
    public void RemoveLayer_ReturnsFalseForForeignLayer()
    {
        var doc = new Document(100, 100);
        var stranger = new Layer("NotInDoc");
        Assert.False(doc.RemoveLayer(stranger));
    }
}

public class LayerTests
{
    [Fact]
    public void Defaults_AreSane()
    {
        var layer = new Layer();
        Assert.True(layer.IsVisible);
        Assert.Equal(1.0, layer.Opacity);
        Assert.Equal(BlendMode.Normal, layer.Blend);
        Assert.False(layer.IsLocked);
        Assert.Null(layer.Pixels);
    }

    [Fact]
    public void Constructor_TrimsName_EmptyBecomesDefault()
    {
        Assert.Equal("Base", new Layer("  Base  ").Name);
        Assert.Equal("Layer", new Layer("").Name);
        Assert.Equal("Layer", new Layer("   ").Name);
        Assert.Equal("Layer", new Layer(null as string ?? "").Name);
    }

    [Fact]
    public void BlendMode_AllValuesValid()
    {
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            Assert.True(mode.IsValid(), $"{mode} should be valid");
        }
    }
}

public class UndoStackTests
{
    [Fact]
    public void EmptyStack_CannotUndoOrRedo()
    {
        var stack = new UndoStack();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);
        stack.Undo(); // no-op, must not throw
        stack.Redo(); // no-op, must not throw
    }

    [Fact]
    public void Push_ClearsRedo()
    {
        var stack = new UndoStack();
        var doc = new Document(100, 100);
        stack.Push(new AddLayerCommand(doc, new Layer("A")));
        stack.Undo();
        Assert.True(stack.CanRedo);
        stack.Push(new AddLayerCommand(doc, new Layer("B")));
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void AddLayerCommand_RoundTrips()
    {
        var doc = new Document(100, 100);
        var layer = new Layer("Test");
        var stack = new UndoStack();
        stack.Push(new AddLayerCommand(doc, layer));

        stack.Undo();
        Assert.Empty(doc.Layers);
        stack.Redo();
        Assert.Single(doc.Layers);
        Assert.Same(layer, doc.Layers[0]);
    }

    [Fact]
    public void RemoveLayerCommand_RoundTrips_PreservesIndex()
    {
        var doc = new Document(100, 100);
        var a = new Layer("A");
        var b = new Layer("B");
        var c = new Layer("C");
        doc.AddLayer(a);
        doc.AddLayer(b);
        doc.AddLayer(c);

        var stack = new UndoStack();
        stack.Push(new RemoveLayerCommand(doc, b));
        stack.Undo();

        Assert.Equal(3, doc.Layers.Count);
        Assert.Same(b, doc.Layers[1]); // b is back in the middle
    }
}
