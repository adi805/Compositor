namespace Compositor.Core.Imaging;

/// <summary>
/// JPEG export options, 1:1 with upstream <c>JPEGOptions</c> in
/// <c>IO/ImageExporter.swift</c>: quality in 0...1 (default 0.85) plus an opaque
/// matte colour for transparency, stored as sRGB components in 0...1 the way the
/// upstream colour picker writes them.
/// </summary>
public sealed record JpegOptions
{
    /// Upstream default <c>var quality: Double = 0.85</c>.
    public const double DefaultQuality = 0.85;

    /// Encoding quality in 0...1. Upstream clamps at encode time; we normalise on the way in.
    public double Quality { get; init; } = DefaultQuality;

    /// Matte red component (upstream <c>red</c>), sRGB in 0...1.
    public double Red { get; init; } = 1;

    /// Matte green component (upstream <c>green</c>), sRGB in 0...1.
    public double Green { get; init; } = 1;

    /// Matte blue component (upstream <c>blue</c>), sRGB in 0...1.
    public double Blue { get; init; } = 1;

    /// Clamps every component to 0...1, falling back per-field when a value is not finite.
    public JpegOptions Normalize() => this with
    {
        Quality = ClampUnit(Quality, DefaultQuality),
        Red = ClampUnit(Red, 1),
        Green = ClampUnit(Green, 1),
        Blue = ClampUnit(Blue, 1),
    };

    /// The 0-100 integer the encoder takes; rounded away from zero like the upstream sheet readout.
    public int QualityPercent => (int)Math.Round(ClampUnit(Quality, DefaultQuality) * 100, MidpointRounding.AwayFromZero);

    /// Matte as 8-bit channels for <see cref="MatteComposite"/>.
    public (byte R, byte G, byte B) MatteBytes => (To8(Red), To8(Green), To8(Blue));

    /// The sheet's "62%" style readout, invariant so it never depends on the host culture.
    public string QualityReadout => QualityPercent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static double ClampUnit(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : fallback;

    private static byte To8(double value) =>
        (byte)Math.Clamp((int)Math.Round(ClampUnit(value, 1) * 255, MidpointRounding.AwayFromZero), 0, 255);
}
