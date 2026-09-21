namespace Compositor.Core.Adjustments;

/// <summary>
/// The six Photoshop color ranges plus Master. Default bands are upstream's
/// starting hues (falloff start, range start, range end, falloff end, degrees).
/// </summary>
public enum ColorRange
{
    Master,
    Reds,
    Yellows,
    Greens,
    Cyans,
    Blues,
    Magentas,
}

/// <summary>
/// A hue band in degrees wrapping at 360: full strength inside [RangeStart, RangeEnd],
/// fading linearly through the falloff shoulders. Port of upstream <c>HueBand</c>.
/// </summary>
public struct HueBand
{
    public double FalloffStart { get; set; }
    public double RangeStart { get; set; }
    public double RangeEnd { get; set; }
    public double FalloffEnd { get; set; }

    public HueBand(double falloffStart, double rangeStart, double rangeEnd, double falloffEnd)
    {
        FalloffStart = falloffStart;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        FalloffEnd = falloffEnd;
    }

    public static HueBand DefaultFor(ColorRange range) => range switch
    {
        ColorRange.Master => new HueBand(0, 0, 360, 360),
        ColorRange.Reds => new HueBand(315, 345, 15, 45),
        ColorRange.Yellows => new HueBand(15, 45, 75, 105),
        ColorRange.Greens => new HueBand(75, 105, 135, 165),
        ColorRange.Cyans => new HueBand(135, 165, 195, 225),
        ColorRange.Blues => new HueBand(195, 225, 255, 285),
        ColorRange.Magentas => new HueBand(255, 285, 315, 345),
        _ => new HueBand(0, 0, 360, 360),
    };

    /// <summary>Degrees from <paramref name="from"/> forward to <paramref name="to"/>, always 0..360.</summary>
    public static double Forward(double from, double to)
    {
        var delta = (to - from) % 360;
        return delta < 0 ? delta + 360 : delta;
    }

    /// <summary>1 inside the range, linear ramp through each shoulder, 0 outside.</summary>
    public readonly double WeightOf(double hue)
    {
        var span = Forward(FalloffStart, FalloffEnd);
        if (span <= 0)
        {
            return 1; // Master covers everything.
        }

        var position = Forward(FalloffStart, hue);
        if (position > span)
        {
            return 0;
        }

        var rampIn = Forward(FalloffStart, RangeStart);
        var plateauEnd = Forward(FalloffStart, RangeEnd);
        if (position < rampIn)
        {
            return rampIn > 0 ? position / rampIn : 1;
        }

        if (position <= plateauEnd)
        {
            return 1;
        }

        var rampOut = span - plateauEnd;
        return rampOut > 0 ? (span - position) / rampOut : 1;
    }

    public readonly double[] Handles => [FalloffStart, RangeStart, RangeEnd, FalloffEnd];

    /// <summary>Keeps all four handles in 0..360 and the band under a full circle.</summary>
    public readonly HueBand Normalized()
    {
        static double Wrap(double value)
        {
            var r = value % 360;
            return r < 0 ? r + 360 : r;
        }

        var result = this;
        result.FalloffStart = Wrap(FalloffStart);
        result.RangeStart = Wrap(RangeStart);
        result.RangeEnd = Wrap(RangeEnd);
        result.FalloffEnd = Wrap(FalloffEnd);
        if (Forward(result.FalloffStart, result.FalloffEnd) > 350)
        {
            result.FalloffEnd = Wrap(result.FalloffStart + 350);
        }

        return result;
    }

    /// <summary>Moves one handle (0..3), keeping the four in order and the band under a full circle.</summary>
    public HueBand WithHandle(int index, double degrees)
    {
        var updated = this;
        var value = ((degrees % 360) + 360) % 360;
        switch (index)
        {
            case 0: updated.FalloffStart = value; break;
            case 1: updated.RangeStart = value; break;
            case 2: updated.RangeEnd = value; break;
            default: updated.FalloffEnd = value; break;
        }

        var span = Forward(updated.FalloffStart, updated.FalloffEnd);
        var toStart = Forward(updated.FalloffStart, updated.RangeStart);
        var toEnd = Forward(updated.FalloffStart, updated.RangeEnd);
        if (span <= 1 || span > 350 || toStart > toEnd || toEnd > span)
        {
            return this;
        }

        return updated;
    }
}

/// <summary>Per-range hue/saturation/lightness adjustments (degrees / percent / percent).</summary>
public record struct RangeAdjustment(double Hue, double Saturation, double Lightness)
{
    public static readonly RangeAdjustment None = new(0, 0, 0);
}

/// <summary>
/// Hue/Saturation state: per-range values and bands, Master applies everywhere.
/// Exact port of upstream <c>HueSaturationSettings</c>.
/// </summary>
public sealed class HueSaturationSettings
{
    public ColorRange Range { get; set; } = ColorRange.Master;
    public bool Colorize { get; set; }

    /// <summary>Applies the selected range to everything outside its band instead.</summary>
    public bool InvertRange { get; set; }

    public Dictionary<ColorRange, RangeAdjustment> Adjustments { get; set; } = [];
    public Dictionary<ColorRange, HueBand> Bands { get; set; } = [];

    public HueSaturationSettings(
        double hue = 0, double saturation = 0, double lightness = 0,
        bool colorize = false, ColorRange range = ColorRange.Master)
    {
        Range = range;
        Colorize = colorize;
        Adjustments[range] = new RangeAdjustment(hue, saturation, lightness);
    }

    public double Hue
    {
        get => Adjustments.TryGetValue(Range, out var a) ? a.Hue : 0;
        set
        {
            var a = Adjustments.TryGetValue(Range, out var existing) ? existing : RangeAdjustment.None;
            Adjustments[Range] = a with { Hue = value };
        }
    }

    public double Saturation
    {
        get => Adjustments.TryGetValue(Range, out var a) ? a.Saturation : 0;
        set
        {
            var a = Adjustments.TryGetValue(Range, out var existing) ? existing : RangeAdjustment.None;
            Adjustments[Range] = a with { Saturation = value };
        }
    }

    public double Lightness
    {
        get => Adjustments.TryGetValue(Range, out var a) ? a.Lightness : 0;
        set
        {
            var a = Adjustments.TryGetValue(Range, out var existing) ? existing : RangeAdjustment.None;
            Adjustments[Range] = a with { Lightness = value };
        }
    }

    public HueBand Band
    {
        get => Bands.TryGetValue(Range, out var b) ? b : HueBand.DefaultFor(Range);
        set => Bands[Range] = value;
    }

    /// <summary>Photoshop's starting point when Colorize switches on.</summary>
    public static HueSaturationSettings ColorizeStart => new(0, 25, 0, colorize: true);

    public bool IsIdentity
    {
        get
        {
            if (Colorize)
            {
                return false;
            }

            foreach (var a in Adjustments.Values)
            {
                if (a != RangeAdjustment.None)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>How much a range applies to one hue: Master everywhere, others through their band.</summary>
    public double WeightOf(ColorRange colorRange, double hue)
    {
        if (colorRange == ColorRange.Master)
        {
            return 1;
        }

        var weight = Bands.TryGetValue(colorRange, out var b) ? b : HueBand.DefaultFor(colorRange);
        var w = weight.WeightOf(hue);
        return InvertRange && colorRange == Range ? 1 - w : w;
    }

    /// <summary>Per-degree hue response, sampled once per degree to keep pixel work cheap.</summary>
    public (double Shift, double Saturation, double Lightness)[] HueResponse()
    {
        var table = new (double Shift, double Saturation, double Lightness)[361];
        for (var degree = 0; degree <= 360; degree++)
        {
            double shift = 0, saturation = 0, lightness = 0;
            foreach (var (colorRange, adjustment) in Adjustments)
            {
                if (adjustment == RangeAdjustment.None)
                {
                    continue;
                }

                var weight = WeightOf(colorRange, degree);
                if (weight <= 0)
                {
                    continue;
                }

                shift += adjustment.Hue * weight;
                saturation += adjustment.Saturation * weight;
                lightness += adjustment.Lightness * weight;
            }

            table[degree] = (shift, saturation, lightness);
        }

        return table;
    }

    /// <summary>
    /// Adjusts one color (0..1 components) through HSL: per-range shifts weighted by the
    /// original hue, multiplicative saturation (grays stay gray), lightness pulled toward
    /// white/black, or full colorize. Exact port of upstream <c>HueSaturationFilter.adjust</c>.
    /// </summary>
    public (double R, double G, double B) AdjustPixel(
        double red, double green, double blue, (double Shift, double Saturation, double Lightness)[]? response = null)
    {
        var (hue, saturation, lightness) = ToHsl(red, green, blue);
        double lightnessAmount;
        if (Colorize)
        {
            hue = Hue % 360;
            saturation = Math.Min(1, Math.Max(0, Saturation / 100));
            lightnessAmount = Lightness / 100;
        }
        else
        {
            var table = response ?? HueResponse();
            var sampled = table[Math.Min(table.Length - 1, Math.Max(0, (int)Math.Round(hue)))];
            lightnessAmount = sampled.Lightness / 100;
            hue = (hue + sampled.Shift) % 360;
            if (hue < 0)
            {
                hue += 360;
            }

            saturation = Math.Min(1, Math.Max(0, saturation * (1 + (sampled.Saturation / 100))));
        }

        var amount = Math.Min(1, Math.Max(-1, lightnessAmount));
        lightness = amount >= 0 ? lightness + ((1 - lightness) * amount) : lightness * (1 + amount);
        return ToRgb(hue, saturation, Math.Min(1, Math.Max(0, lightness)));
    }

    public static (double H, double S, double L) ToHsl(double red, double green, double blue)
    {
        var high = Math.Max(red, Math.Max(green, blue));
        var low = Math.Min(red, Math.Min(green, blue));
        var lightness = (high + low) / 2;
        var delta = high - low;
        if (delta <= 0)
        {
            return (0, 0, lightness);
        }

        var saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
        double hue;
        if (high == red)
        {
            hue = (green - blue) / delta;
        }
        else if (high == green)
        {
            hue = ((blue - red) / delta) + 2;
        }
        else
        {
            hue = (red - green) / delta + 4;
        }

        hue *= 60;
        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, Math.Min(1, saturation), lightness);
    }

    public static (double R, double G, double B) ToRgb(double hue, double saturation, double lightness)
    {
        if (saturation <= 0)
        {
            return (lightness, lightness, lightness);
        }

        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var sector = hue / 60;
        var second = chroma * (1 - Math.Abs((sector % 2) - 1));
        var basis = lightness - chroma / 2;
        double r, g, b;
        switch ((int)Math.Floor(sector))
        {
            case 0: r = chroma; g = second; b = 0; break;
            case 1: r = second; g = chroma; b = 0; break;
            case 2: r = 0; g = chroma; b = second; break;
            case 3: r = 0; g = second; b = chroma; break;
            case 4: r = second; g = 0; b = chroma; break;
            default: r = chroma; g = 0; b = second; break;
        }

        return (
            Math.Min(1, Math.Max(0, r + basis)),
            Math.Min(1, Math.Max(0, g + basis)),
            Math.Min(1, Math.Max(0, b + basis)));
    }
}
