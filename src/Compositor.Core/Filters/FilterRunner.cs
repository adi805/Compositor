using Compositor.Core.Adjustments;
using Compositor.Core.Selection;

namespace Compositor.Core.Filters;

/// <summary>
/// Runs the Filter-menu operations on a layer surface, mirroring upstream's
/// PixelFilter: Gaussian Blur and Motion Blur get generous transparent room to
/// spread into (so a layer's own border never fades) and are cut back to the
/// layer's bounds afterwards; Add Noise and Lens Correction run in place; Grain
/// is the Image-menu adjustment form. Every result is optionally blended back
/// through a selection as coverage × adjusted + (1 − coverage) × original.
/// Blur/motion math runs premultiplied, matching Core Image's treatment.
/// </summary>
public static class FilterRunner
{
    /// <summary>CIMotionBlur's radius per pixel of streak length (spread of an even streak of length d is d/√12).</summary>
    public static double MotionRadiusPerPixel => 1.0 / Math.Sqrt(12.0);

    /// <summary>Remove Distortion at ±100 moves the image's corners by this share of their distance from the center.</summary>
    public const double LensStrength = 0.35;

    /// <summary>The room a blur needs around the layer: about three standard deviations, or half a streak.</summary>
    public static int BlurMargin(FilterKind kind, FilterSettings settings) => kind switch
    {
        FilterKind.GaussianBlur => (int)Math.Ceiling((settings.Radius * 3) + 2),
        FilterKind.MotionBlur => (int)Math.Ceiling((settings.Distance / 2) + 2),
        _ => 0,
    };

    /// <summary>Runs a filter and returns the new surface; the input is never modified.</summary>
    public static RasterSurface Apply(FilterKind kind, RasterSurface surface, FilterSettings settings, SelectionClip? selection, uint? seed = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(settings);
        var s = settings.Normalize();
        if (kind is FilterKind.RemoveBackground)
        {
            throw new NotSupportedException(
                "RemoveBackground needs a measured subject matte and the layer-mask subsystem; it is not available yet.");
        }

        RasterSurface result;
        switch (kind)
        {
            case FilterKind.GaussianBlur:
            {
                var margin = BlurMargin(kind, s);
                var padded = Pad(surface, margin);
                BlurGaussian(padded, s.Radius);
                result = CropBack(padded, margin, surface.Width, surface.Height);
                break;
            }

            case FilterKind.MotionBlur:
            {
                var margin = BlurMargin(kind, s);
                var padded = Pad(surface, margin);
                BlurMotion(padded, s.Angle, s.Distance);
                result = CropBack(padded, margin, surface.Width, surface.Height);
                break;
            }

            case FilterKind.AddNoise:
            {
                result = new RasterSurface(surface.Width, surface.Height);
                Array.Copy(surface.Pixels, result.Pixels, surface.Pixels.Length);
                FilterKernels.AddNoise(result.Pixels, surface.Width, surface.Height,
                    (float)s.Amount, s.Gaussian, s.Monochromatic, seed ?? DefaultSeed);
                break;
            }

            case FilterKind.LensCorrection:
            {
                result = new RasterSurface(surface.Width, surface.Height);
                if (s.Distortion == 0)
                {
                    Array.Copy(surface.Pixels, result.Pixels, surface.Pixels.Length);
                    break;
                }

                var k = (s.Distortion / 100.0) * LensStrength;
                FilterKernels.LensDistort(surface.Pixels, result.Pixels, surface.Width, surface.Height, k);
                break;
            }

            case FilterKind.ContentAwareFill:
            {
                // The selection names the hole, not the editable area. Everything outside it is
                // left alone by the blend below, and the kernel never writes there anyway, so a
                // failed fill returns the layer as it was.
                result = new RasterSurface(surface.Width, surface.Height);
                Array.Copy(surface.Pixels, result.Pixels, surface.Pixels.Length);
                Imaging.ContentFill.Fill(result.Pixels, surface.Width, surface.Height, selection ?? SelectionClip.Empty);
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown filter kind.");
        }

        AdjustmentRunner.BlendThroughSelection(result, surface, selection);
        return result;
    }

    /// <summary>
    /// Film grain (Image-menu adjustment upstream): applies in place on a copy.
    /// `seed` fixes the pattern; `unitsPerPixel` scales grain size with any
    /// preview downscale so a preview shows the grain at the right coarseness.
    /// </summary>
    public static RasterSurface ApplyGrain(RasterSurface surface, GrainSettings settings, SelectionClip? selection,
        double unitsPerPixel = 1.0, uint? seed = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid)
        {
            throw new ArgumentException("Grain settings are invalid.", nameof(settings));
        }

        var result = new RasterSurface(surface.Width, surface.Height);
        Array.Copy(surface.Pixels, result.Pixels, surface.Pixels.Length);
        if (settings.Amount > 0)
        {
            var s = settings.Normalize();
            FilterKernels.AdjustGrain(result.Pixels, surface.Width, surface.Height,
                s.Amount, s.Size, s.Roughness, seed ?? s.Seed, 0, 0, unitsPerPixel);
        }

        AdjustmentRunner.BlendThroughSelection(result, surface, selection);
        return result;
    }

    /// <summary>
    /// Seed used when a caller does not supply one. Upstream hands every filter its seed on the job
    /// (Filters.swift's FilterJob.seed), so noise and grain repeat per application. It is a parameter
    /// here for the same reason, not process-wide state a parallel test could race.
    /// </summary>
    public const uint DefaultSeed = 0x436F6D70; // "Comp"

    // ------------------------------------------------------------ blur core

    private static RasterSurface Pad(RasterSurface source, int margin)
    {
        var width = source.Width + (2 * margin);
        var height = source.Height + (2 * margin);
        var padded = new RasterSurface(width, height);
        for (var y = 0; y < source.Height; y++)
        {
            Array.Copy(source.Pixels, y * source.Width * 4,
                padded.Pixels, ((y + margin) * width + margin) * 4, source.Width * 4);
        }

        return padded;
    }

    private static RasterSurface CropBack(RasterSurface padded, int margin, int width, int height)
    {
        var result = new RasterSurface(width, height);
        for (var y = 0; y < height; y++)
        {
            Array.Copy(padded.Pixels, ((y + margin) * padded.Width + margin) * 4,
                result.Pixels, y * width * 4, width * 4);
        }

        return result;
    }

    /// <summary>Straight-alpha RGBA → premultiplied float plane set.</summary>
    private static float[] ToPremultiplied(RasterSurface surface)
    {
        var planes = new float[surface.Pixels.Length];
        var src = surface.Pixels;
        for (var i = 0; i < src.Length; i += 4)
        {
            var a = src[i + 3] / 255f;
            planes[i] = src[i] * a;
            planes[i + 1] = src[i + 1] * a;
            planes[i + 2] = src[i + 2] * a;
            planes[i + 3] = src[i + 3];
        }

        return planes;
    }

    /// <summary>Premultiplied float planes → straight-alpha RGBA bytes.</summary>
    private static void FromPremultiplied(float[] planes, RasterSurface target)
    {
        var dst = target.Pixels;
        for (var i = 0; i < dst.Length; i += 4)
        {
            var a = planes[i + 3];
            dst[i + 3] = (byte)Math.Clamp(MathF.Round(a, MidpointRounding.AwayFromZero), 0, 255);
            if (a <= 0)
            {
                dst[i] = dst[i + 1] = dst[i + 2] = 0;
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                dst[i + c] = (byte)Math.Clamp(MathF.Round(planes[i + c] * 255f / a, MidpointRounding.AwayFromZero), 0, 255);
            }
        }
    }

    /// <summary>
    /// In-place separable Gaussian blur with sigma = radius over premultiplied
    /// float planes. Edges are unclamped: outside the surface is transparent, so
    /// a blur softens the layer's edge and spreads into the padded room.
    /// </summary>
    private static void BlurGaussian(RasterSurface surface, double sigma)
    {
        if (sigma <= 0)
        {
            return;
        }

        var radius = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var kernel = new float[radius + 1];
        var sum = 0f;
        for (var i = 0; i <= radius; i++)
        {
            kernel[i] = MathF.Exp((float)(-(i * i) / (2 * sigma * sigma)));
            sum += i == 0 ? kernel[i] : kernel[i] * 2;
        }

        for (var i = 0; i <= radius; i++)
        {
            kernel[i] /= sum;
        }

        var planes = ToPremultiplied(surface);
        var temp = new float[planes.Length];
        var w = surface.Width;
        var h = surface.Height;
        Horizontal(planes, temp, w, h, kernel, radius);
        Vertical(temp, planes, w, h, kernel, radius);
        FromPremultiplied(planes, surface);
    }

    /// <summary>
    /// In-place motion blur: a Gaussian-tapered line kernel along `angleDegrees`
    /// counterclockwise from horizontal (Photoshop's convention, with the y axis
    /// pointing up as on screen), sigma = distance/√12 so the streak's spread
    /// matches an even smear of that length. Bilinear sampling, edges unclamped.
    /// </summary>
    private static void BlurMotion(RasterSurface surface, double angleDegrees, double distance)
    {
        var sigma = distance * MotionRadiusPerPixel;
        var angle = angleDegrees * Math.PI / 180.0;
        var dx = Math.Cos(angle);
        var dy = -Math.Sin(angle); // our rows run top-down; counterclockwise goes up on screen
        var extent = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        // Taps every half pixel so short streaks still smear.
        var taps = (extent * 2) * 2 + 1;
        var kernel = new float[taps];
        var sum = 0f;
        for (var i = 0; i < taps; i++)
        {
            var t = (i - (taps - 1) / 2.0) * 0.5;
            kernel[i] = MathF.Exp((float)(-(t * t) / (2 * sigma * sigma)));
            sum += kernel[i];
        }

        for (var i = 0; i < taps; i++)
        {
            kernel[i] /= sum;
        }

        var planes = ToPremultiplied(surface);
        var output = new float[planes.Length];
        var w = surface.Width;
        var h = surface.Height;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var i = 0; i < taps; i++)
                {
                    var t = (i - (taps - 1) / 2.0) * 0.5;
                    var sx = x + (t * dx);
                    var sy = y + (t * dy);
                    var weight = kernel[i];
                    if (weight <= 0)
                    {
                        continue;
                    }

                    var (pr, pg, pb, pa) = SampleBilinear(planes, w, h, sx, sy);
                    r += weight * pr;
                    g += weight * pg;
                    b += weight * pb;
                    a += weight * pa;
                }

                var o = ((y * w) + x) * 4;
                output[o] = r;
                output[o + 1] = g;
                output[o + 2] = b;
                output[o + 3] = a;
            }
        }

        FromPremultiplied(output, surface);
    }

    private static (float R, float G, float B, float A) SampleBilinear(float[] planes, int w, int h, double x, double y)
    {
        var x0 = Math.Floor(x);
        var y0 = Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var ix = (int)x0;
        var iy = (int)y0;
        float r = 0, g = 0, b = 0, a = 0;
        for (var j = 0; j < 2; j++)
        {
            var row = iy + j;
            if (row < 0 || row >= h)
            {
                continue;
            }

            var wy = (float)(j != 0 ? fy : 1 - fy);
            for (var i = 0; i < 2; i++)
            {
                var column = ix + i;
                if (column < 0 || column >= w)
                {
                    continue;
                }

                var wx = (float)(i != 0 ? fx : 1 - fx);
                var weight = wx * wy;
                if (weight == 0)
                {
                    continue;
                }

                var o = ((row * w) + column) * 4;
                r += weight * planes[o];
                g += weight * planes[o + 1];
                b += weight * planes[o + 2];
                a += weight * planes[o + 3];
            }
        }

        return (r, g, b, a);
    }

    private static void Horizontal(float[] src, float[] dst, int w, int h, float[] kernel, int radius)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var i = -radius; i <= radius; i++)
                {
                    var sx = x + i;
                    if (sx < 0 || sx >= w)
                    {
                        continue; // unclamped: outside is transparent
                    }

                    var weight = kernel[Math.Abs(i)];
                    var o = ((y * w) + sx) * 4;
                    r += weight * src[o];
                    g += weight * src[o + 1];
                    b += weight * src[o + 2];
                    a += weight * src[o + 3];
                }

                var t = ((y * w) + x) * 4;
                dst[t] = r;
                dst[t + 1] = g;
                dst[t + 2] = b;
                dst[t + 3] = a;
            }
        }
    }

    private static void Vertical(float[] src, float[] dst, int w, int h, float[] kernel, int radius)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var i = -radius; i <= radius; i++)
                {
                    var sy = y + i;
                    if (sy < 0 || sy >= h)
                    {
                        continue;
                    }

                    var weight = kernel[Math.Abs(i)];
                    var o = ((sy * w) + x) * 4;
                    r += weight * src[o];
                    g += weight * src[o + 1];
                    b += weight * src[o + 2];
                    a += weight * src[o + 3];
                }

                var t = ((y * w) + x) * 4;
                dst[t] = r;
                dst[t + 1] = g;
                dst[t + 2] = b;
                dst[t + 3] = a;
            }
        }
    }
}
