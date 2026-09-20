namespace Compositor.Core;

/// <summary>
/// Blend modes supported by Compositor. Mirrors the 9 modes in upstream
/// robbietilton/Compositor (see docs/references in that repo).
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
}

public static class BlendModeExtensions
{
    public static bool IsValid(this BlendMode mode) =>
        mode is >= BlendMode.Normal and <= BlendMode.ColorBurn;
}
