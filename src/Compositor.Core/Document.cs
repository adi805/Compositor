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
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Print resolution in DPI (upstream manifest resolution, default 72).</summary>
    public double Resolution { get; set; } = 72;

    /// <summary>Active layer, serialized as activeLayerUUID. Null is valid.</summary>
    public Guid? ActiveLayerId { get; set; }

    /// <summary>
    /// Active selection (upstream DocumentSelection). Null = no selection
    /// (edits touch everything); non-null empty = touch nothing.
    /// Session state, intentionally not serialized to .comp v1.
    /// </summary>
    public Selection.DocumentSelection? Selection { get; set; }

    public List<Layer> Layers { get; } = new();

    public Document(int width, int height, Guid? id = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Id = id ?? Guid.NewGuid();
    }

    public void AddLayer(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Layers.Add(layer);
    }

    public bool RemoveLayer(Layer layer) => Layers.Remove(layer);

    /// <summary>
    /// Canvas bounds, upstream limit 1..30000 per side (CanvasSizeOptions/ImageResizer).
    /// Called by geometry commands only.
    /// </summary>
    public void SetSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 30_000);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 30_000);
        Width = width;
        Height = height;
    }
}
