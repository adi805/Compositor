namespace Compositor.Core;

/// <summary>
/// A Compositor document: ordered stack of layers plus canvas metadata.
/// Mirrors the document model in upstream robbietilton/Compositor.
/// </summary>
public sealed class Document
{
    /// <summary>Stable identity used by project files (.comp manifest).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "Untitled";
    public int Width { get; }
    public int Height { get; }

    /// <summary>Active layer, serialized as activeLayerUUID. Null is valid.</summary>
    public Guid? ActiveLayerId { get; set; }

    public List<Layer> Layers { get; } = new();

    public Document(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
    }

    public void AddLayer(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Layers.Add(layer);
    }

    public bool RemoveLayer(Layer layer) => Layers.Remove(layer);
}
