using Compositor.Core;

namespace Compositor.Core.Commands;

/// <summary>Adds a layer to a document, removable via undo.</summary>
public sealed class AddLayerCommand : IUndoCommand
{
    private readonly Document _doc;
    private readonly Layer _layer;

    public AddLayerCommand(Document doc, Layer layer)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
    }

    public void Redo() => _doc.AddLayer(_layer);
    public void Undo() => _doc.RemoveLayer(_layer);
}

/// <summary>Removes a layer from a document, restorable via undo.</summary>
public sealed class RemoveLayerCommand : IUndoCommand
{
    private readonly Document _doc;
    private readonly Layer _layer;
    private readonly int _index;

    public RemoveLayerCommand(Document doc, Layer layer)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _index = doc.Layers.IndexOf(layer);
        if (_index < 0) throw new ArgumentException("Layer is not in this document", nameof(layer));
    }

    public void Undo()
    {
        _doc.Layers.Insert(Math.Min(_index, _doc.Layers.Count), _layer);
    }

    public void Redo() => _doc.RemoveLayer(_layer);
}
