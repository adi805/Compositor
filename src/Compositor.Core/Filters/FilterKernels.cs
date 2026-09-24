namespace Compositor.Core.Filters;

/// <summary>
/// Bit-exact C# ports of upstream's C pixel kernels: NoisePixels.c (Add Noise),
/// LensPixels.c (Lens Correction) and AdjustPixels.c's adjust_grain (Film
/// Grain). The noise and grain hashes are position-and-seed fixed, so the same
/// seed always gives the same pattern — identical to the Mac build.
/// Surfaces are straight-alpha RGBA; the kernels unpremultiply/repremultiply
/// exactly like the C originals do.
/// </summary>
public static class FilterKernels
{
    // ---------------------------------------------------------------- noise

    /// <summary>A well-mixed 32-bit hash, so neighbouring pixels get unrelated values.</summary>
    private static uint NoiseHash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352dU;
        x ^= x >> 15;
        x *= 0x846ca68bU;
        x ^= x >> 16;
        return x;
    }

    /// <summary>Uniform in [0, 1).</summary>
    private static float NoiseUnit(uint key) => (NoiseHash(key) >> 8) * (1f / 16777216f);

    /// <summary>
    /// Adds noise to the color of RGBA pixels (4 bytes per pixel), leaving alpha
    /// untouched and fully transparent pixels alone. `amount` is Photoshop's
    /// percentage: uniform noise spans ±amount% of half the range, Gaussian noise
    /// has a standard deviation of two thirds of that. Monochromatic adds the
    /// same value to all three channels. Direct port of noise_add().
    /// </summary>
    public static void AddNoise(byte[] rgba, int width, int height, float amount, bool gaussian, bool monochromatic, uint seed)
    {
        var spread = amount / 100f * 127.5f;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = ((y * width) + x) * 4;
                var alpha = rgba[p + 3];
                if (alpha == 0)
                {
                    continue;
                }

                var baseHash = NoiseHash(seed ^ NoiseHash((uint)((y * width) + x)));
                for (var c = 0; c < 3; c++)
                {
                    var key = monochromatic ? baseHash : baseHash + ((uint)c * 0x9e3779b9U);
                    float n;
                    if (gaussian)
                    {
                        // Box–Muller: two uniform values make one normally distributed one.
                        var u1 = NoiseUnit(key);
                        var u2 = NoiseUnit(key ^ 0x68e31da4U);
                        n = MathF.Sqrt(-2f * MathF.Log(1f - u1)) * MathF.Cos(6.2831853f * u2) * spread * (2f / 3f);
                    }
                    else
                    {
                        n = ((NoiseUnit(key) * 2f) - 1f) * spread;
                    }

                    var value = (rgba[p + c] * 255f / alpha) + n;
                    value = value < 0 ? 0 : value > 255 ? 255 : value;
                    rgba[p + c] = (byte)MathF.Round(value * alpha / 255f, MidpointRounding.AwayFromZero);
                }
            }
        }
    }

    // ----------------------------------------------------------------- lens

    /// <summary>
    /// Radial lens distortion over RGBA. Each destination pixel samples the
    /// source bilinearly at its offset from the image center scaled by
    /// (1 − k·r²), where r is that offset relative to the half-diagonal:
    /// k &gt; 0 pulls samples inward (straightens barrel distortion, corners
    /// crop), k &lt; 0 pushes them outward (straightens pincushion distortion,
    /// corners turn transparent). Pixels outside the source are transparent.
    /// k = 0 copies the source exactly. Direct port of lens_distort().
    /// </summary>
    public static void LensDistort(ReadOnlySpan<byte> source, byte[] destination, int width, int height, double k)
    {
        var cx = width * 0.5;
        var cy = height * 0.5;
        var halfDiagonal2 = (cx * cx) + (cy * cy);
        for (var y = 0; y < height; y++)
        {
            var dy = (y + 0.5) - cy;
            for (var x = 0; x < width; x++)
            {
                var dx = (x + 0.5) - cx;
                var scale = 1.0 - (k * ((dx * dx) + (dy * dy)) / halfDiagonal2);
                // Source position in pixel-center coordinates.
                var sx = cx + (dx * scale) - 0.5;
                var sy = cy + (dy * scale) - 0.5;
                var fx0 = Math.Floor(sx);
                var fy0 = Math.Floor(sy);
                var fx = sx - fx0;
                var fy = sy - fy0;
                var x0 = (long)fx0;
                var y0 = (long)fy0;
                double sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                for (var j = 0; j < 2; j++)
                {
                    var row = y0 + j;
                    if (row < 0 || row >= height)
                    {
                        continue;
                    }

                    var wy = j != 0 ? fy : 1 - fy;
                    if (wy == 0)
                    {
                        continue;
                    }

                    for (var i = 0; i < 2; i++)
                    {
                        var column = x0 + i;
                        if (column < 0 || column >= width)
                        {
                            continue;
                        }

                        var weight = wy * (i != 0 ? fx : 1 - fx);
                        if (weight == 0)
                        {
                            continue;
                        }

                        var sp = (int)(((row * width) + column) * 4);
                        sumR += weight * source[sp];
                        sumG += weight * source[sp + 1];
                        sumB += weight * source[sp + 2];
                        sumA += weight * source[sp + 3];
                    }
                }

                var o = ((y * width) + x) * 4;
                destination[o] = (byte)Math.Round(sumR, MidpointRounding.AwayFromZero);
                destination[o + 1] = (byte)Math.Round(sumG, MidpointRounding.AwayFromZero);
                destination[o + 2] = (byte)Math.Round(sumB, MidpointRounding.AwayFromZero);
                destination[o + 3] = (byte)Math.Round(sumA, MidpointRounding.AwayFromZero);
            }
        }
    }

    // ---------------------------------------------------------------- grain

    private static uint Mix32(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352dU;
        x ^= x >> 15;
        x *= 0x846ca68bU;
        x ^= x >> 16;
        return x;
    }

    /// <summary>
    /// A value in −1…1 for an integer lattice point, fixed by the point and the
    /// seed. Two uniform halves summed give a triangular spread, closer to film
    /// grain than flat noise.
    /// </summary>
    private static float Lattice(long ix, long iy, uint seed)
    {
        var h = Mix32((uint)ix * 0x9E3779B1U ^ Mix32((uint)iy * 0x85EBCA77U ^ seed));
        return ((h & 0xFFFFU) / 65535f) + ((h >> 16) / 65535f) - 1f;
    }

    private static float Clamp255(float value) => value < 0 ? 0 : value > 255 ? 255 : value;

    /// <summary>
    /// Film grain: brightness noise, strongest in the midtones, from smooth
    /// value-noise lattice plus per-pixel roughness. origin and unitsPerPixel
    /// place the image's pixels in document space. Direct port of adjust_grain().
    /// </summary>
    public static void AdjustGrain(byte[] rgba, int width, int height, double amount, double size, double roughness, uint seed, double originX, double originY, double unitsPerPixel)
    {
        if (!(amount > 0) || !(unitsPerPixel > 0))
        {
            return;
        }

        if (!(size > 0))
        {
            size = 1;
        }

        var strength = (float)(amount > 100 ? 1.0 : amount / 100.0) * 0.35f * 255f;
        var rough = (float)(roughness < 0 ? 0.0 : roughness > 100 ? 1.0 : roughness / 100.0);
        var fineSeed = Mix32(seed ^ 0xA511E9B3U);
        for (var y = 0; y < height; y++)
        {
            var v = originY + ((y + 0.5) * unitsPerPixel);
            var cellY = Math.Floor(v / size);
            var ty = (float)(v / size - cellY);
            ty = (ty * ty * ((3f - (2f * ty))));
            var iy = (long)cellY;
            var fineY = (long)Math.Floor(v);
            for (var x = 0; x < width; x++)
            {
                var p = ((y * width) + x) * 4;
                var a = rgba[p + 3];
                if (a == 0)
                {
                    continue;
                }

                var u = originX + ((x + 0.5) * unitsPerPixel);
                var cellX = Math.Floor(u / size);
                var tx = (float)(u / size - cellX);
                tx = tx * tx * (3f - (2f * tx));
                var ix = (long)cellX;
                var n00 = Lattice(ix, iy, seed);
                var n10 = Lattice(ix + 1, iy, seed);
                var n01 = Lattice(ix, iy + 1, seed);
                var n11 = Lattice(ix + 1, iy + 1, seed);
                var top = n00 + ((n10 - n00) * tx);
                var bottom = n01 + ((n11 - n01) * tx);
                // Blending neighbors narrows the spread; scaling restores about the lattice's own.
                var smooth = (top + ((bottom - top) * ty)) * 1.6f;
                var fine = Lattice((long)Math.Floor(u), fineY, fineSeed);
                var noise = smooth + ((fine - smooth) * rough);
                var unpremultiply = a == 255 ? 1f : 255f / a;
                var r = rgba[p] * unpremultiply;
                var g = rgba[p + 1] * unpremultiply;
                var b = rgba[p + 2] * unpremultiply;
                var level = ((0.2126f * r) + (0.7152f * g) + (0.0722f * b)) / 255f;
                if (level > 1)
                {
                    level = 1;
                }

                // Film grain shows most in the midtones.
                var delta = noise * strength * (0.4f + (2.4f * level * (1f - level)));
                var coverage = a / 255f;
                rgba[p] = (byte)((Clamp255(r + delta) * coverage) + 0.5f);
                rgba[p + 1] = (byte)((Clamp255(g + delta) * coverage) + 0.5f);
                rgba[p + 2] = (byte)((Clamp255(b + delta) * coverage) + 0.5f);
            }
        }
    }
}
