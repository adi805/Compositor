namespace Compositor.Core;

/// <summary>
/// Blend modes supported by Compositor. Mirrors all 13 modes of upstream
/// robbietilton/Compositor (Document/LayerAppearance.swift: LayerBlendMode),
/// in the same order as upstream's Codable raw values.
/// Hue/Saturation/Color/Luminosity are non-separable (HSL-based per the
/// W3C Compositing spec, matching the CoreImage kernels upstream uses).
/// </summary>
public enum BlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    Difference,
    ColorDodge,
    ColorBurn,
    Hue,
    Saturation,
    Color,
    Luminosity,
}

public static class BlendModeExtensions
{
    public static bool IsValid(this BlendMode mode) =>
        mode is >= BlendMode.Normal and <= BlendMode.Luminosity;
}
