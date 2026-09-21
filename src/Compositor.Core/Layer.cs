namespace Compositor.Core;

/// <summary>
/// A single layer in a Compositor document. Raster-only for now;
/// text and shape layers arrive in a later phase.
/// </summary>
public sealed class Layer
{
    /// <summary>Stable identity used by project files (.comp manifest).</summary>
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Name { get; set; } = "Layer";

    /// <summary>Parent group. Null = root level. Must reference a layer with IsGroup.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// True when this layer is a folder that groups its children.
    /// Groups carry no pixels of their own (upstream: isGroup implies imageFile nil).
    /// </summary>
    public bool IsGroup { get; set; }

    public bool IsVisible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public BlendMode Blend { get; set; } = BlendMode.Normal;
    public bool IsLocked { get; set; }
    public LayerTransform Transform { get; set; }

    /// <summary>
    /// Pixel data for this layer. Null until the raster engine lands;
    /// document model tracks the slot so undo/redo and project I/O already work.
    /// Layers without pixels are "blank layers" and serialize without an image asset.
    /// </summary>
    public RasterSurface? Pixels { get; set; }

    public Layer() { }

    public Layer(string name, Guid? id = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name.Trim();
        Id = id ?? Guid.NewGuid();
    }

    /// <summary>Creates a group folder node (no pixels, ever).</summary>
    public static Layer Group(string? name = null, Guid? id = null) =>
        new(name ?? "Group", id) { IsGroup = true, Pixels = null };
}
