using System;

namespace Compositor.Core.Filters;

/// <summary>
/// Guided image filtering (He, Sun and Tang) in C#: a mask pulled onto the edges of the image it
/// came from, which is what recovers hair and fur that a segmentation model cuts straight through.
///
/// This is upstream's <c>Document/GuidedMatte.swift</c>, ported arithmetic for arithmetic. It is not
/// machine learning and needs no model: the mean-of-square-window, the per-window slope and offset,
/// and the final clamped linear map are all written out. Upstream does the same on purpose - its own
/// comment says Core Image's <c>CIGuidedFilter</c> does nothing on that system, so it computes the
/// filter itself rather than trusting a framework primitive.
///
/// Two behaviours are deliberate and easy to "fix" by accident:
/// <list type="bullet">
/// <item>Window sums clamp the index instead of shrinking the window, so the divisor is always
/// <c>2r+1</c> even at the border. Edge pixels therefore average replicated copies of themselves.</item>
/// <item><see cref="Apply"/> is the whole algorithm, including the smoothing of slope and offset,
/// which is what keeps the result free of blocky seams.</item>
/// </list>
/// </summary>
public static class GuidedFilter
{
    /// <summary>The constant upstream hard-codes in <c>refine</c>. Small enough to follow fine strands.</summary>
    public const float DefaultEpsilon = 1e-4f;

    /// <summary>
    /// Mean over a (2r+1)^2 square, as two running-sum passes so the cost does not grow with radius.
    /// Out-of-range indices clamp to the edge, matching upstream's <c>min/max</c> on every access.
    /// </summary>
    public static float[] Box(ReadOnlySpan<float> source, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"plane must be positive sized, got {width}x{height}");
        }

        if (source.Length != width * height)
        {
            throw new ArgumentException($"expected {width * height} values, got {source.Length}");
        }

        if (radius < 0)
        {
            throw new ArgumentException($"radius must not be negative, got {radius}", nameof(radius));
        }

        var span = (float)((radius * 2) + 1);
        var pass = new float[width * height];

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var sum = 0f;
            for (var k = -radius; k <= radius; k++)
            {
                sum += source[row + Clamp(k, width)];
            }

            for (var x = 0; x < width; x++)
            {
                pass[row + x] = sum / span;
                sum -= source[row + Clamp(x - radius, width)];
                sum += source[row + Clamp(x + radius + 1, width)];
            }
        }

        var result = new float[width * height];
        for (var x = 0; x < width; x++)
        {
            var sum = 0f;
            for (var k = -radius; k <= radius; k++)
            {
                sum += pass[Clamp(k, height) * width + x];
            }

            for (var y = 0; y < height; y++)
            {
                result[(y * width) + x] = sum / span;
                sum -= pass[Clamp(y - radius, height) * width + x];
                sum += pass[Clamp(y + radius + 1, height) * width + x];
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="mask"/> refined by <paramref name="guide"/>, both 0..1 and the same size. A
    /// bigger radius reaches further for detail; <paramref name="epsilon"/> decides how much of an
    /// edge in the guide counts, so a small one follows fine strands.
    /// </summary>
    public static float[] Apply(
        ReadOnlySpan<float> mask,
        ReadOnlySpan<float> guide,
        int width,
        int height,
        int radius,
        float epsilon)
    {
        var count = width * height;
        if (mask.Length != count || guide.Length != count)
        {
            throw new ArgumentException($"mask ({mask.Length}) and guide ({guide.Length}) must both be {count} values");
        }

        var meanGuide = Box(guide, width, height, radius);
        var meanMask = Box(mask, width, height, radius);

        var squares = new float[count];
        var products = new float[count];
        for (var i = 0; i < count; i++)
        {
            squares[i] = guide[i] * guide[i];
            products[i] = guide[i] * mask[i];
        }

        var meanSquares = Box(squares, width, height, radius);
        var meanProducts = Box(products, width, height, radius);

        var slope = new float[count];
        var offset = new float[count];
        for (var i = 0; i < count; i++)
        {
            var variance = meanSquares[i] - (meanGuide[i] * meanGuide[i]);
            var covariance = meanProducts[i] - (meanGuide[i] * meanMask[i]);
            slope[i] = covariance / (variance + epsilon);
            offset[i] = meanMask[i] - (slope[i] * meanGuide[i]);
        }

        var meanSlope = Box(slope, width, height, radius);
        var meanOffset = Box(offset, width, height, radius);

        var result = new float[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = Math.Clamp((meanSlope[i] * guide[i]) + meanOffset[i], 0f, 1f);
        }

        return result;
    }

    /// <summary>
    /// The working size upstream refines at: a copy no larger than <paramref name="limit"/> on its
    /// longest side, then drawn back up. Fine detail still comes from the guide, and a preview stays
    /// quick to redraw while a slider moves.
    /// </summary>
    public static double ScaleFactor(int width, int height, double limit)
    {
        var longest = Math.Max(width, height);
        if (longest <= 0)
        {
            throw new ArgumentException($"plane must be positive sized, got {width}x{height}");
        }

        return Math.Min(1d, limit / longest);
    }

    /// <summary>Width and height of that working copy, never below one pixel.</summary>
    public static (int Width, int Height) WorkingSize(int width, int height, double limit)
    {
        var factor = ScaleFactor(width, height, limit);
        return (
            Math.Max(1, (int)RoundAwayFromZero(width * factor)),
            Math.Max(1, (int)RoundAwayFromZero(height * factor)));
    }

    /// <summary>
    /// Radius in working-copy terms. Upstream shrinks it with the copy and floors it at one, so a
    /// zero slider still reaches one pixel rather than silently disabling the filter.
    /// </summary>
    public static int EffectiveRadius(double radius, double limit, int width, int height)
    {
        var steps = (int)RoundAwayFromZero(radius * ScaleFactor(width, height, limit));
        return Math.Max(1, steps);
    }

    private static double RoundAwayFromZero(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static int Clamp(int index, int length) => Math.Min(length - 1, Math.Max(0, index));
}
