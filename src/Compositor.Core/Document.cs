namespace Compositor.Core;

/// <summary>
/// A Compositor document: ordered stack of layers plus canvas metadata.
/// Mirrors the document model in upstream robbietilton/Compositor.
/// </summary>
public sealed class Document
{
    public string Name { get; set; } = "Untitled";
    public int Width { get; }
    public int Height { get; }
    public List<Layer> Layers { get; } = new();

    public Document(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
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
