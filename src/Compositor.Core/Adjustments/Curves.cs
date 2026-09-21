namespace Compositor.Core.Adjustments;

/// <summary>One Curves handle in 0..255 space. Upstream <c>CurvePoint</c>.</summary>
public readonly record struct CurvePoint(double X, double Y);

/// <summary>
/// Curves state: four point lists (RGB + R/G/B), shape-preserving cubic Hermite
/// interpolation without overshoot. Exact port of upstream <c>CurvesSettings</c>.
/// </summary>
public sealed class CurvesSettings
{
    public LevelsChannel Channel { get; set; } = LevelsChannel.Rgb;

    public CurvePoint[][] Channels { get; set; } =
    [
        [new CurvePoint(0, 0), new CurvePoint(255, 255)],
        [new CurvePoint(0, 0), new CurvePoint(255, 255)],
        [new CurvePoint(0, 0), new CurvePoint(255, 255)],
        [new CurvePoint(0, 0), new CurvePoint(255, 255)],
    ];

    public bool IsValid
    {
        get
        {
            if (Channels.Length != 4)
            {
                return false;
            }

            foreach (var points in Channels)
            {
                if (points.Length < 2 || points.Length > 32)
                {
                    return false;
                }

                if (points[0].X != 0 || points[^1].X != 255)
                {
                    return false;
                }

                foreach (var p in points)
                {
                    if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.X > 255 || p.Y < 0 || p.Y > 255)
                    {
                        return false;
                    }
                }

                for (var i = 1; i < points.Length; i++)
                {
                    if (points[i - 1].X >= points[i].X)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Curve value at x (0..255) for a channel index (0 = RGB composite). Shape-preserving
    /// cubic Hermite: segment slopes are harmonic means, zeroed at direction changes, so
    /// the curve never overshoots between handles.
    /// </summary>
    public double Value(double x, int channel)
    {
        var p = Channels[channel];
        var last = -1;
        for (var i = p.Length - 1; i >= 0; i--)
        {
            if (p[i].X <= x)
            {
                last = i;
                break;
            }
        }

        var index = Math.Min(p.Length - 2, Math.Max(0, last < 0 ? 0 : last));
        var d = new double[p.Length - 1];
        for (var i = 0; i < d.Length; i++)
        {
            d[i] = (p[i + 1].Y - p[i].Y) / (p[i + 1].X - p[i].X);
        }

        double Slope(int j)
        {
            if (j == 0)
            {
                return d[0];
            }

            if (j == p.Length - 1)
            {
                return d[^1];
            }

            if ((d[j - 1] * d[j]) <= 0)
            {
                return 0;
            }

            return 2 / (1 / d[j - 1] + 1 / d[j]);
        }

        var h = p[index + 1].X - p[index].X;
        var t = Math.Min(1, Math.Max(0, (x - p[index].X) / h));
        var y = (((2 * t * t * t) - (3 * t * t) + 1) * p[index].Y)
            + (((t * t * t) - (2 * t * t) + t) * h * Slope(index))
            + (((-2 * t * t * t) + (3 * t * t)) * p[index + 1].Y)
            + (((t * t * t) - (t * t)) * h * Slope(index + 1));
        return Math.Min(255, Math.Max(0, y));
    }

    /// <summary>
    /// Builds the 3×256 lookup table (0..1 floats). Each channel's curve composes with
    /// the RGB curve, matching upstream's table construction.
    /// </summary>
    public float[] BuildTables()
    {
        var tables = new float[768];
        for (var c = 1; c <= 3; c++)
        {
            for (var i = 0; i < 256; i++)
            {
                tables[((c - 1) * 256) + i] = (float)(Value(Value(i, c), 0) / 255d);
            }
        }

        return tables;
    }
}
