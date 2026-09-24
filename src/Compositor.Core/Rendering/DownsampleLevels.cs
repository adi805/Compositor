using Compositor.Core.Imaging;

namespace Compositor.Core.Rendering;

/// <summary>
/// Level arithmetic behind the downsample cache: how many exact halvings to draw from when an image lands
/// at a given size on screen, and which source pixels each reduced pixel covers.
/// Ported from <c>Rendering/DownsampleCache.swift</c> (<c>level(for:)</c>, <c>maxLevel</c>, the halving rule).
/// </summary>
/// <remarks>
/// A one-step resample only looks at a few neighbouring pixels, so shrinking an image 4x or 8x comes out
/// soft or grainy. Keeping a chain of halved copies means a draw only ever leaves the last reduction of at
/// most 2x to the scaler. Every halving is exactly 2x, so level k pixel i always covers source pixels
/// i*2^k ..&lt; (i+1)*2^k, and a piece of an image reduced on its own lines up with the whole image reduced.
/// </remarks>
public static class DownsampleLevels
{
    /// <summary>Most halvings ever used; past this the scaler does the rest.</summary>
    public const int MaxLevel = 6;

    /// <summary>Pixels of halved copies kept at once (about 400 MB of RGBA at the default limit).</summary>
    public static long PixelBudget => ImageBudget.MaxSurfacePixels;

    /// <summary>
    /// Halvings to draw from when an image lands <paramref name="factor"/> output pixels per image pixel:
    /// the most that still leave the copy at least that large (0 from half size up).
    /// </summary>
    public static int For(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0d || factor >= 0.5d) return 0;
        return Math.Min(MaxLevel, (int)Math.Floor(Math.Log2(1d / factor)));
    }

    /// <summary>
    /// Size after one halving, rounded up: a copy reaches up to 2^level - 1 source pixels past the right
    /// and bottom edges.
    /// </summary>
    public static (int Width, int Height) Halve(int width, int height)
        => ((width + 1) / 2, (height + 1) / 2);

    /// <summary>Size after <paramref name="level"/> halvings.</summary>
    public static (int Width, int Height) AtLevel(int width, int height, int level)
    {
        for (var i = 0; i < level && i < MaxLevel; i++) (width, height) = Halve(width, height);
        return (width, height);
    }

    /// <summary>
    /// Source pixel range that level-<paramref name="level"/> pixel <paramref name="index"/> covers, as
    /// <c>[Start, End)</c>. Exact because every halving is exactly 2x.
    /// </summary>
    public static (int Start, int End) Covers(int level, int index)
    {
        var span = 1 << Math.Clamp(level, 0, MaxLevel);
        var start = index * span;
        return (start, start + span);
    }
}
