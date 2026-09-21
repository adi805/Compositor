namespace Compositor.Core.Adjustments;

/// <summary>Levels channel selector, ported from upstream Levels.swift.</summary>
public enum LevelsChannel
{
    Rgb,
    Red,
    Green,
    Blue,
}

/// <summary>
/// One Levels range: input black/white points, gamma, and output remap.
/// Exact port of upstream <c>LevelRange</c> including its clamping rules.
/// </summary>
public struct LevelRange
{
    public double Black { get; set; } = 0;
    public double Gamma { get; set; } = 1;
    public double White { get; set; } = 255;
    public double OutputBlack { get; set; } = 0;
    public double OutputWhite { get; set; } = 255;

    public LevelRange(double black, double gamma, double white, double outputBlack, double outputWhite)
    {
        Black = black;
        Gamma = gamma;
        White = white;
        OutputBlack = outputBlack;
        OutputWhite = outputWhite;
    }

    public static LevelRange Identity => new(0, 1, 255, 0, 255);

    public LevelRange Normalized()
    {
        static double Clamp(double n, double lo, double hi, double fallback) =>
            double.IsFinite(n) ? Math.Min(hi, Math.Max(lo, n)) : fallback;

        var result = this;
        result.Black = Clamp(Black, 0, 254, 0);
        result.White = Clamp(White, result.Black + 1, 255, 255);
        result.Gamma = Clamp(Gamma, 0.1, 9.99, 1);
        result.OutputBlack = Clamp(OutputBlack, 0, 255, 0);
        result.OutputWhite = Clamp(OutputWhite, 0, 255, 255);
        return result;
    }

    /// <summary>Maps a 0..1 sample through this range; returns 0..1.</summary>
    public double Apply(double value)
    {
        var s = Normalized();
        var input = Math.Min(1, Math.Max(0, (value * 255 - s.Black) / (s.White - s.Black)));
        return (s.OutputBlack + (Math.Pow(input, 1 / s.Gamma) * (s.OutputWhite - s.OutputBlack))) / 255;
    }
}

/// <summary>
/// Levels state: per-channel ranges followed by the composite RGB adjustment.
/// Exact port of upstream <c>LevelsSettings</c>.
/// </summary>
public sealed class LevelsSettings
{
    public LevelsChannel Channel { get; set; } = LevelsChannel.Rgb;

    /// <summary>Four ranges: RGB, Red, Green, Blue (upstream order).</summary>
    public LevelRange[] Ranges { get; set; } =
    [
        LevelRange.Identity, LevelRange.Identity, LevelRange.Identity, LevelRange.Identity,
    ];

    public LevelRange Current
    {
        get => Ranges[(int)Channel];
        set => Ranges[(int)Channel] = value.Normalized();
    }

    public bool IsIdentity
    {
        get
        {
            var identity = LevelRange.Identity;
            foreach (var range in Ranges)
            {
                if (!range.Normalized().Equals(identity))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Individual channels, followed by the composite RGB adjustment.</summary>
    public double Apply(double value, LevelsChannel channel) =>
        Ranges[0].Apply(Ranges[(int)channel].Apply(value));

    /// <summary>
    /// Builds the 3×256 lookup table (0..1 floats, R/G/B concatenated) the LUT
    /// kernel consumes, matching upstream's flat table layout.
    /// </summary>
    public float[] BuildTables()
    {
        var tables = new float[768];
        for (var c = 0; c < 3; c++)
        {
            var channel = (LevelsChannel)(c + 1);
            for (var i = 0; i < 256; i++)
            {
                tables[(c * 256) + i] = (float)Apply(i / 255d, channel);
            }
        }

        return tables;
    }

    /// <summary>Eyedropper sampling: black/gray/white points calibrated across RGB. Upstream <c>sampling</c>.</summary>
    public LevelsSettings Sampling(double[] rgb, LevelsSampleMode mode)
    {
        var result = new LevelsSettings { Channel = Channel };
        for (var c = 1; c <= 3; c++)
        {
            var range = result.Ranges[c];
            var v = rgb[c - 1] * 255;
            switch (mode)
            {
                case LevelsSampleMode.Black:
                    range.Black = Math.Min(range.White - 1, Math.Max(0, v));
                    break;
                case LevelsSampleMode.White:
                    range.White = Math.Max(range.Black + 1, Math.Min(255, v));
                    break;
                case LevelsSampleMode.Gray:
                    var fraction = (v - range.Black) / (range.White - range.Black);
                    if (fraction > 0 && fraction < 1)
                    {
                        range.Gamma = Math.Log(fraction) / Math.Log(0.5);
                    }
                    else
                    {
                        continue;
                    }

                    break;
            }

            range.OutputBlack = 0;
            range.OutputWhite = 255;
            result.Ranges[c] = range.Normalized();
        }

        result.Ranges[0] = LevelRange.Identity;
        return result;
    }
}

/// <summary>Levels eyedroppers, ported from upstream <c>LevelsSample</c>.</summary>
public enum LevelsSampleMode
{
    Black,
    Gray,
    White,
}

/// <summary>Auto-levels strategies, exact port of upstream <c>LevelsAuto</c>.</summary>
public enum LevelsAuto
{
    Contrast,
    Color,
    Neutral,
}

public static class LevelsAutoExtensions
{
    /// <summary>
    /// Auto settings from a 4×256 histogram (RGB + R/G/B). Endpoints sit at the 0.1%
    /// cumulative tails; Contrast shares one interval to preserve channel relationships,
    /// Neutral additionally neutralizes the weighted mean via gamma.
    /// </summary>
    public static LevelsSettings Settings(this LevelsAuto auto, double[][] histogram)
    {
        var result = new LevelsSettings();

        static (double Low, double High)? Endpoints(double[] bins)
        {
            double total = 0;
            foreach (var b in bins)
            {
                total += b;
            }

            if (total <= 0)
            {
                return null;
            }

            double sum = 0;
            var low = 255;
            var high = 0;
            for (var i = 0; i < 256; i++)
            {
                sum += bins[i];
                if (sum > total * 0.001)
                {
                    low = i;
                    break;
                }
            }

            sum = 0;
            for (var i = 255; i >= 0; i--)
            {
                sum += bins[i];
                if (sum > total * 0.001)
                {
                    high = i;
                    break;
                }
            }

            return low < high ? ((double)low, (double)high) : null;
        }

        if (auto == LevelsAuto.Contrast)
        {
            var limits = new List<(double Low, double High)>();
            for (var c = 1; c < histogram.Length; c++)
            {
                if (Endpoints(histogram[c]) is { } e)
                {
                    limits.Add(e);
                }
            }

            if (limits.Count > 0)
            {
                var low = limits.Min(l => l.Low);
                var high = limits.Max(l => l.High);
                if (low < high)
                {
                    result.Ranges[0] = new LevelRange(low, 1, high, 0, 255);
                }
            }
        }
        else
        {
            for (var c = 1; c <= 3; c++)
            {
                if (Endpoints(histogram[c]) is not { } ep)
                {
                    continue;
                }

                var range = new LevelRange(ep.Low, 1, ep.High, 0, 255);
                if (auto == LevelsAuto.Neutral)
                {
                    double total = 0;
                    foreach (var b in histogram[c])
                    {
                        total += b;
                    }

                    double weighted = 0;
                    for (var i = 0; i < 256; i++)
                    {
                        weighted += range.Apply(i / 255d) * histogram[c][i];
                    }

                    var mean = total > 0 ? weighted / total : 0;
                    if (mean > 0 && mean < 1)
                    {
                        range.Gamma = Math.Min(9.99, Math.Max(0.1, Math.Log(mean) / Math.Log(0.5)));
                    }
                }

                result.Ranges[c] = range;
            }
        }

        return result;
    }
}

public static class LevelsHistogram
{
    /// <summary>
    /// 4×256 histogram (RGB + R/G/B). RGB is the mean of the three channel histograms,
    /// not a luminance histogram; transparent pixels contribute nothing; an optional
    /// selection coverage scales each pixel's weight. Upstream <c>levels_histogram</c>.
    /// </summary>
    public static double[][] Compute(RasterSurface surface, Selection.SelectionClip? selection)
    {
        var bins = new double[4][];
        for (var c = 0; c < 4; c++)
        {
            bins[c] = new double[256];
        }

        var pixels = surface.Pixels;
        var count = surface.Width * surface.Height;
        for (var i = 0; i < count; i++)
        {
            var a = pixels[(i * 4) + 3];
            if (a == 0)
            {
                continue;
            }

            var weight = a / 255d;
            if (selection is not null)
            {
                weight *= selection.FactorAt(i % surface.Width, i / surface.Width);
                if (weight <= 0)
                {
                    continue;
                }
            }

            for (var channel = 0; channel < 3; channel++)
            {
                var value = Math.Min(255, (int)pixels[(i * 4) + channel]);
                bins[channel + 1][value] += weight;
                bins[0][value] += weight / 3.0;
            }
        }

        return bins;
    }

    /// <summary>
    /// Display scaling with spike capping (upstream <c>LevelsHistogramDisplay.scale</c>):
    /// keep linear bin ratios but cap isolated spikes at 4× the 95th-percentile interior
    /// peak so solid backgrounds do not flatten the plot.
    /// </summary>
    public static double DisplayScale(double[] bins)
    {
        double peak = 0;
        foreach (var b in bins)
        {
            if (double.IsFinite(b) && b > peak)
            {
                peak = b;
            }
        }

        if (peak <= 0)
        {
            return 0;
        }

        var interior = new List<double>();
        for (var i = 1; i < bins.Length - 1; i++)
        {
            if (double.IsFinite(bins[i]) && bins[i] > 0)
            {
                interior.Add(bins[i]);
            }
        }

        if (interior.Count == 0)
        {
            return peak;
        }

        interior.Sort();
        var typicalPeak = interior[(int)((interior.Count - 1) * 0.95)];
        return Math.Min(peak, typicalPeak * 4);
    }
}
