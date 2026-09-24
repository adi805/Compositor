using System;
using Compositor.Core.Selection;

namespace Compositor.Core.Imaging;

/// <summary>
/// Content-aware inpainting: fills selected pixels by copying the best-matching patch from
/// unselected, fully opaque neighbours. A port of the upstream <c>Rendering/ContentFill.c</c>
/// kernel, which is why it looks unusual: candidates come from a coherent-offset propagation
/// step plus random donors, scored on mean squared difference over a (2r+1)^2 patch, then
/// refined by a shrinking random search.
/// </summary>
/// <remarks>
/// The pixel buffer is RGBA, tightly packed, the same shape every filter kernel here takes.
/// The generator is a fixed-seed linear congruential one, so a given image and mask produce a
/// given result. The port mirrors the draw sequence exactly: candidates rejected by a bounds
/// check still consume their draws, because the source draws before it checks. Keep that
/// order; tidying it changes which pixel wins and breaks the reproduction.
/// </remarks>
public static class ContentFill
{
    /// <summary>
    /// Fills every selected pixel of <paramref name="rgba"/> in place.
    /// </summary>
    /// <param name="selection">
    /// The pixels to fill, which is the inverse of what a selection means to every other
    /// filter here: those restrict <em>editing</em> to the selection, this one designates the
    /// hole. Unselected transparent pixels are neither source nor target, so a transparent
    /// canvas does not smear into the result.
    /// </param>
    /// <returns>False when no patch was usable, in which case nothing was written.</returns>
    public static bool Fill(byte[] rgba, int width, int height, SelectionClip selection)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A fill needs a non-empty image.");
        }

        if (rgba.Length < (width * height * 4))
        {
            throw new ArgumentException("The buffer is shorter than the image it claims to hold.", nameof(rgba));
        }

        var n = width * height;
        var known = new byte[n];
        var target = new byte[n];
        var valid = new byte[n];
        var queued = new byte[n];
        var donors = new int[n];
        var queue = new int[n];
        var chosen = new int[n];
        Array.Fill(chosen, -1);

        var radius = width >= 5 && height >= 5 ? 2 : 0;
        var missing = 0;

        // Selected pixels are filled. Unselected opaque pixels are the image to match and copy
        // from; unselected transparent ones are neither: nothing to match against, and left as
        // they are.
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = (y * width) + x;
                var isTarget = selection.FactorAt(x, y) != 0f;
                target[p] = isTarget ? (byte)1 : (byte)0;
                known[p] = isTarget || rgba[(p * 4) + 3] != 255 ? (byte)0 : (byte)1;
                if (isTarget)
                {
                    missing++;
                }
            }
        }

        if (missing == 0)
        {
            return true;
        }

        var donorCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = (y * width) + x;
                if (known[p] == 0)
                {
                    continue;
                }

                var ok = true;
                for (var dy = -radius; dy <= radius && ok; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sx = x + dx;
                        var sy = y + dy;
                        if (sx < 0 || sy < 0 || sx >= width || sy >= height || known[(sy * width) + sx] == 0)
                        {
                            ok = false;
                            break;
                        }
                    }
                }

                if (ok)
                {
                    valid[p] = 1;
                    donors[donorCount++] = p;
                }
            }
        }

        if (donorCount == 0)
        {
            return false;
        }

        var tail = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = (y * width) + x;
                if (target[p] != 0 && TouchesKnown(known, width, height, p, x, y))
                {
                    queue[tail++] = p;
                    queued[p] = 1;
                }
            }
        }

        var seed = 0x6d2b79f5u;
        var head = 0;
        var scan = 0;
        Span<int> neighbours = stackalloc int[4];
        while (true)
        {
            while (head < tail)
            {
                var p = queue[head++];
                var x = p % width;
                var y = p / width;
                var best = -1;
                var score = double.MaxValue;

                neighbours[0] = x > 0 ? p - 1 : -1;
                neighbours[1] = x + 1 < width ? p + 1 : -1;
                neighbours[2] = y > 0 ? p - width : -1;
                neighbours[3] = y + 1 < height ? p + width : -1;

                // Propagate coherent source offsets, then refine with randomized patch search.
                for (var k = 0; k < 28; k++)
                {
                    var q = -1;
                    if (k < 4)
                    {
                        var t = neighbours[k];
                        if (t >= 0)
                        {
                            q = (chosen[t] >= 0 ? chosen[t] : t) + (p - t);
                        }
                    }
                    else
                    {
                        q = donors[(int)(NextRandom(ref seed) % (uint)donorCount)];
                    }

                    if (q < 0 || q >= n || valid[q] == 0)
                    {
                        continue;
                    }

                    var s = Match(rgba, width, known, p, q, radius);
                    if (best < 0 || s < score)
                    {
                        score = s;
                        best = q;
                    }
                }

                if (best < 0)
                {
                    best = donors[0];
                }

                for (var r = 64; r >= 1; r /= 2)
                {
                    var qx = (best % width) + (int)(NextRandom(ref seed) % ((2 * r) + 1)) - r;
                    var qy = (best / width) + (int)(NextRandom(ref seed) % ((2 * r) + 1)) - r;
                    if (qx < 0 || qy < 0 || qx >= width || qy >= height || valid[(qy * width) + qx] == 0)
                    {
                        continue;
                    }

                    var q = (qy * width) + qx;
                    var s = Match(rgba, width, known, p, q, radius);
                    if (s < score)
                    {
                        score = s;
                        best = q;
                    }
                }

                var src = ((best / width) * width + (best % width)) * 4;
                var dst = p * 4;
                rgba[dst] = rgba[src];
                rgba[dst + 1] = rgba[src + 1];
                rgba[dst + 2] = rgba[src + 2];
                rgba[dst + 3] = rgba[src + 3];
                known[p] = 1;
                chosen[p] = best;

                for (var k = 0; k < 4; k++)
                {
                    var q = neighbours[k];
                    if (q >= 0 && target[q] != 0 && known[q] == 0 && queued[q] == 0)
                    {
                        queued[q] = 1;
                        queue[tail++] = q;
                    }
                }
            }

            // A selected area that only transparency touches starts from the first such pixel,
            // then spreads. Without this, a hole surrounded by nothing would be skipped.
            while (scan < n && (target[scan] == 0 || known[scan] != 0))
            {
                scan++;
            }

            if (scan >= n)
            {
                break;
            }

            queue[tail++] = scan;
            queued[scan] = 1;
        }

        return true;
    }

    private static bool TouchesKnown(byte[] known, int width, int height, int p, int x, int y) =>
        (x > 0 && known[p - 1] != 0)
        || (x + 1 < width && known[p + 1] != 0)
        || (y > 0 && known[p - width] != 0)
        || (y + 1 < height && known[p + width] != 0);

    /// <summary>
    /// Mean squared difference over a (2r+1)^2 patch centred on two pixels, counting only
    /// positions where both fit inside the image and the destination's is already known.
    /// </summary>
    private static double Match(byte[] rgba, int width, byte[] known, int p, int q, int radius)
    {
        var px = p % width;
        var py = p / width;
        var qx = q % width;
        var qy = q / width;
        var count = 0;
        var sum = 0.0;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = px + dx;
                var y = py + dy;
                var sx = qx + dx;
                var sy = qy + dy;
                // Row bounds are expressed through the linear index, because a packed row index
                // past the last row is exactly what "outside the image" means here.
                if (x < 0 || y < 0 || x >= width || ((y * width) + x) >= known.Length || sx < 0 || sy < 0
                    || sx >= width || ((sy * width) + sx) >= known.Length || known[(y * width) + x] == 0)
                {
                    continue;
                }

                var a = ((y * width) + x) * 4;
                var b = ((sy * width) + sx) * 4;
                for (var c = 0; c < 4; c++)
                {
                    var d = rgba[a + c] - rgba[b + c];
                    sum += d * d;
                }

                count++;
            }
        }

        return count != 0 ? sum / count : double.MaxValue;
    }

    private static uint NextRandom(ref uint state)
    {
        state = unchecked((state * 1664525u) + 1013904223u);
        return state;
    }
}
