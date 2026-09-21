namespace Compositor.Core.Adjustments;

/// <summary>A straight sRGB color stored with an adjustment, 0..1 per channel. Upstream <c>AdjustmentColor</c>.</summary>
public readonly record struct AdjustmentColor(double Red, double Green, double Blue)
{
    public static readonly AdjustmentColor Black = new(0, 0, 0);
    public static readonly AdjustmentColor White = new(1, 1, 1);

    public bool IsValid => double.IsFinite(Red) && double.IsFinite(Green) && double.IsFinite(Blue)
        && Red is >= 0 and <= 1 && Green is >= 0 and <= 1 && Blue is >= 0 and <= 1;

    public AdjustmentColor Clamped() => new(
        double.IsFinite(Red) ? Math.Min(1, Math.Max(0, Red)) : 0,
        double.IsFinite(Green) ? Math.Min(1, Math.Max(0, Green)) : 0,
        double.IsFinite(Blue) ? Math.Min(1, Math.Max(0, Blue)) : 0);

    public byte Scaled() => (byte)Math.Min(255, Math.Max(0, Math.Round(this.Red * 255)));
}

/// <summary>
/// Photoshop's Exposure: <c>exposure</c> (stops) scales linear light, <c>offset</c> shifts it,
/// gamma correction bends the result. One curve for every channel; alpha kept. Exact port
/// of upstream <c>ExposureSettings</c> including the sRGB decode/encode constants.
/// </summary>
public sealed class ExposureSettings
{
    public const double ExposureMin = -20, ExposureMax = 20;
    public const double OffsetMin = -0.5, OffsetMax = 0.5;
    public const double GammaMin = 0.01, GammaMax = 9.99;

    /// <summary>Stops of light, −20..20.</summary>
    public double Exposure { get; set; }

    /// <summary>Added in linear light, −0.5..0.5.</summary>
    public double Offset { get; set; }

    /// <summary>Gamma correction, 0.01..9.99; above 1 brightens the midtones.</summary>
    public double Gamma { get; set; } = 1;

    public bool IsValid =>
        Exposure is >= ExposureMin and <= ExposureMax
        && Offset is >= OffsetMin and <= OffsetMax
        && Gamma is >= GammaMin and <= GammaMax;

    /// <summary>Each channel's output (0..1) for each input byte: decode, bend, encode.</summary>
    public float[] BuildTable()
    {
        var scale = Math.Pow(2, Exposure);
        var table = new float[256];
        for (var i = 0; i < 256; i++)
        {
            var encoded = i / 255d;
            var linear = encoded <= 0.04045 ? encoded / 12.92 : Math.Pow((encoded + 0.055) / 1.055, 2.4);
            linear = Math.Pow(Math.Max(0, (linear * scale) + Offset), 1 / Gamma);
            var output = linear <= 0.0031308 ? linear * 12.92 : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
            table[i] = (float)Math.Min(1, Math.Max(0, output));
        }

        return table;
    }
}

/// <summary>
/// Gradient Map: each pixel's Rec.709 brightness picks a color between <c>shadows</c> and
/// <c>highlights</c> (reversed swaps the ends); alpha kept. Exact port of upstream
/// <c>GradientMapSettings</c> and its kernel's luma formula.
/// </summary>
public sealed class GradientMapSettings
{
    public AdjustmentColor Shadows { get; set; } = AdjustmentColor.Black;
    public AdjustmentColor Highlights { get; set; } = AdjustmentColor.White;
    public bool Reversed { get; set; }

    public bool IsValid => Shadows.IsValid && Highlights.IsValid;

    public byte[] BuildTable()
    {
        var (dark, light) = Reversed ? (Highlights, Shadows) : (Shadows, Highlights);
        var table = new byte[256 * 3];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255d;
            table[(i * 3)] = (byte)Math.Min(255, Math.Max(0, Math.Round((dark.Red + ((light.Red - dark.Red) * t)) * 255)));
            table[(i * 3) + 1] = (byte)Math.Min(255, Math.Max(0, Math.Round((dark.Green + ((light.Green - dark.Green) * t)) * 255)));
            table[(i * 3) + 2] = (byte)Math.Min(255, Math.Max(0, Math.Round((dark.Blue + ((light.Blue - dark.Blue) * t)) * 255)));
        }

        return table;
    }
}
