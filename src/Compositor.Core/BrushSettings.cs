namespace Compositor.Core;

/// <summary>
/// Brush settings, upstream BrushSettings defaults: diameter 40, hardness 1,
/// opacity caps the whole stroke (overlapping dabs never exceed it, as in
/// Photoshop). Erasing clears the layer's alpha instead of painting color.
/// Upstream has no separate "flow" knob — deposition rate is spacing × falloff —
/// and no stylus pressure in the paint path; both stay out of the model.
/// </summary>
public sealed record BrushSettings
{
    public float Diameter { get; init; } = 40;
    public float Hardness { get; init; } = 1;
    public byte R { get; init; }
    public byte G { get; init; }
    public byte B { get; init; }
    public byte A { get; init; } = 255;
    public float Opacity { get; init; } = 1;
    public bool Erasing { get; init; }

    public float Radius => Diameter / 2f;

    /// <summary>
    /// Upstream BrushStroke init guards: finite hardness in 0..1, positive
    /// diameter, finite opacity in 0..1. Throws at point of use.
    /// </summary>
    public void Validate()
    {
        if (!float.IsFinite(Diameter) || Diameter < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Diameter), "Diameter must be >= 1.");
        }
        if (!float.IsFinite(Hardness) || Hardness is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(Hardness), "Hardness must be 0..1.");
        }
        if (!float.IsFinite(Opacity) || Opacity is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(Opacity), "Opacity must be 0..1.");
        }
    }
}
