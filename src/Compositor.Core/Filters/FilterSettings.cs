namespace Compositor.Core.Filters;

/// <summary>
/// The Filter-menu operations from upstream Filters.swift. The two automatic
/// entries (Remove Background, Content-Aware Fill) need the ML workstream and
/// are declared here for menu parity but throw when run.
/// The four color entries (Curves, Exposure, Gradient Map, Grain) live in the
/// Image/Adjust menu upstream; Curves, Exposure and Gradient Map are already
/// in the adjustments set, Grain joins them here.
/// </summary>
public enum FilterKind
{
    GaussianBlur,
    MotionBlur,
    AddNoise,
    LensCorrection,
    RemoveBackground,
    ContentAwareFill,
}

/// <summary>
/// Every filter's settings; each filter reads only its own. Ranges and
/// defaults match upstream FilterSettings exactly, and <see cref="Normalize"/>
/// clamps non-finite and out-of-range values to the same fallbacks.
/// </summary>
public sealed record FilterSettings
{
    /// <summary>Gaussian Blur radius in layer pixels (the blur's standard deviation), 0.1–250.</summary>
    public double Radius { get; init; } = 1;

    /// <summary>Motion Blur direction in degrees, counterclockwise from horizontal as in Photoshop, −90–90.</summary>
    public double Angle { get; init; }

    /// <summary>Motion Blur streak length in layer pixels, 1–2000.</summary>
    public double Distance { get; init; } = 10;

    /// <summary>Add Noise strength as Photoshop's percentage, 0.1–400.</summary>
    public double Amount { get; init; } = 10;

    /// <summary>Add Noise distribution: Gaussian (more speckled) instead of Uniform.</summary>
    public bool Gaussian { get; init; }

    /// <summary>Add Noise changes brightness only, the same amount on every channel.</summary>
    public bool Monochromatic { get; init; }

    /// <summary>
    /// Lens Correction's Remove Distortion, −100–100: positive straightens barrel
    /// distortion (lines bowing outward), negative straightens pincushion.
    /// </summary>
    public double Distortion { get; init; }

    public FilterSettings Normalize()
    {
        static double Clamp(double value, double lo, double hi, double fallback) =>
            double.IsFinite(value) ? Math.Min(hi, Math.Max(lo, value)) : fallback;

        return new FilterSettings
        {
            Radius = Clamp(Radius, 0.1, 250, 1),
            Angle = Clamp(Angle, -90, 90, 0),
            Distance = Clamp(Distance, 1, 2000, 10),
            Amount = Clamp(Amount, 0.1, 400, 10),
            Gaussian = Gaussian,
            Monochromatic = Monochromatic,
            Distortion = Clamp(Distortion, -100, 100, 0),
        };
    }
}

/// <summary>
/// Film grain settings (an Image-menu adjustment upstream): brightness noise,
/// strongest in the midtones, fixed in document space by <see cref="Seed"/>.
/// Ranges and defaults match upstream GrainSettings.
/// </summary>
public sealed record GrainSettings
{
    public const double AmountMin = 0, AmountMax = 100;
    public const double SizeMin = 0.5, SizeMax = 20;
    public const double RoughnessMin = 0, RoughnessMax = 100;

    /// <summary>Strength, 0–100.</summary>
    public double Amount { get; init; } = 25;

    /// <summary>Grain scale in document pixels, 0.5–20.</summary>
    public double Size { get; init; } = 1.5;

    /// <summary>0–100: how much per-pixel noise roughens the smooth grain.</summary>
    public double Roughness { get; init; } = 50;

    public uint Seed { get; init; }

    public bool IsValid =>
        Amount is >= AmountMin and <= AmountMax
        && Size is >= SizeMin and <= SizeMax
        && Roughness is >= RoughnessMin and <= RoughnessMax;

    public GrainSettings Normalize() => new()
    {
        Amount = Clamp(Amount, AmountMin, AmountMax, 25),
        Size = Clamp(Size, SizeMin, SizeMax, 1.5),
        Roughness = Clamp(Roughness, RoughnessMin, RoughnessMax, 50),
        Seed = Seed,
    };

    private static double Clamp(double value, double lo, double hi, double fallback) =>
        double.IsFinite(value) ? Math.Min(hi, Math.Max(lo, value)) : fallback;
}
