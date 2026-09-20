namespace Compositor.Core;

/// <summary>
/// A single layer in a Compositor document. Raster-only for now;
/// text and shape layers arrive in a later phase.
/// </summary>
public sealed class Layer
{
    /// <summary>Stable identity used by project files (.comp manifest).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public string Name { get; set; } = "Layer";
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

    public Layer(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name.Trim();
    }
}
