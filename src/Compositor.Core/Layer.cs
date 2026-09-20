namespace Compositor.Core;

/// <summary>
/// A single layer in a Compositor document. Raster-only for now (phase 1);
/// text and shape layers arrive in phase 4.
/// </summary>
public sealed class Layer
{
    public string Name { get; set; } = "Layer";
    public bool IsVisible { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public BlendMode Blend { get; set; } = BlendMode.Normal;
    public bool IsLocked { get; set; }

    /// <summary>
    /// Pixel data for this layer. Null until phase 2 gives us a raster engine;
    /// document model tracks the slot so undo/redo and project I/O already work.
    /// </summary>
    public RasterSurface? Pixels { get; set; }

    public Layer() { }

    public Layer(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name.Trim();
    }
}
