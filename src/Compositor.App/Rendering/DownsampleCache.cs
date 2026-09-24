using System.Runtime.InteropServices;
using Compositor.Core;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.App.Rendering;

/// <summary>
/// Sharp reductions of layer surfaces. Resampling a 4000 px surface straight down to 200 px in one step comes
/// out soft, so this keeps a chain of halved copies and lets a draw take the copy closest to the size it
/// lands at. Ported from <c>Rendering/DownsampleCache.swift</c>: exact 2x halvings (so a piece reduced on
/// its own lines up with the whole surface reduced), copies keyed by surface identity, least recently used
/// dropped beyond a pixel budget. Upstream halves with vImage Lanczos; this halves with Skia's high-quality
/// resampler, the nearest equivalent in the pinned SkiaSharp build.
/// </summary>
public sealed class DownsampleCache
{
    public static DownsampleCache Shared { get; } = new();

    private readonly long _pixelBudget;
    private readonly Dictionary<RasterSurface, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly object _gate = new();
    private long _clock;

    public DownsampleCache(long? pixelBudget = null)
        => _pixelBudget = pixelBudget ?? DownsampleLevels.PixelBudget;

    private sealed class Entry
    {
        public required List<RasterSurface> Levels { get; init; }
        public required long Version { get; init; }
        public long LastUse { get; set; }

        public long Pixels
        {
            get
            {
                long total = 0;
                foreach (var level in Levels) total += (long)level.Width * level.Height;
                return total;
            }
        }
    }

    /// <summary>Surfaces with a live chain of halved copies right now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// What to draw when <paramref name="source"/> lands <paramref name="factor"/> output pixels per source
    /// pixel, plus how many halvings were applied.
    /// </summary>
    public (RasterSurface Surface, int Level) For(RasterSurface source, double factor)
        => AtLevel(source, DownsampleLevels.For(factor));

    /// <summary>
    /// <paramref name="source"/> reduced by <paramref name="wanted"/> halvings, and how many were applied
    /// (fewer only if a halving failed or the copy already reached 1 px). The source itself comes back at
    /// level 0.
    /// </summary>
    public (RasterSurface Surface, int Level) AtLevel(RasterSurface source, int wanted)
    {
        if (wanted < 1 || (source.Width <= 1 && source.Height <= 1)) return (source, 0);

        var levels = new List<RasterSurface>();
        lock (_gate)
        {
            _clock++;
            // A surface keeps its identity while painting mutates it in place, so a copy made before the
            // last MarkDirty is stale and has to be thrown away rather than reused.
            if (_entries.TryGetValue(source, out var existing) && existing.Version == source.Version)
            {
                levels = existing.Levels;
            }
        }

        while (levels.Count < wanted)
        {
            var previous = levels.Count == 0 ? source : levels[^1];
            if (previous.Width <= 1 && previous.Height <= 1) break;
            var next = Halve(previous);
            if (next is null) break;
            levels.Add(next);
        }

        if (levels.Count == 0) return (source, 0);

        lock (_gate)
        {
            if (!_entries.TryGetValue(source, out var entry) || entry.Version != source.Version || entry.Levels.Count < levels.Count)
            {
                _entries[source] = new Entry { Levels = levels, Version = source.Version, LastUse = _clock };
            }
            else
            {
                entry.LastUse = _clock;
            }

            Evict(keeping: source);
        }

        var applied = Math.Min(wanted, levels.Count);
        return (levels[applied - 1], applied);
    }

    /// <summary>Drops least recently used chains until the rest fit the budget. Call with the lock held.</summary>
    private void Evict(RasterSurface keeping)
    {
        long total = 0;
        foreach (var entry in _entries.Values) total += entry.Pixels;

        while (total > _pixelBudget && _entries.Count > 1)
        {
            RasterSurface? oldest = null;
            var oldestUse = long.MaxValue;
            foreach (var pair in _entries)
            {
                if (ReferenceEquals(pair.Key, keeping)) continue;
                if (pair.Value.LastUse < oldestUse)
                {
                    oldestUse = pair.Value.LastUse;
                    oldest = pair.Key;
                }
            }

            if (oldest is null) break;
            total -= _entries[oldest].Pixels;
            _entries.Remove(oldest);
        }
    }

    /// <summary>
    /// Exactly half the size, rounded up, resampled at high quality. Colours are read and written
    /// unpremultiplied, which is what <see cref="RasterSurface"/> holds.
    /// </summary>
    private static RasterSurface? Halve(RasterSurface source)
    {
        var (width, height) = DownsampleLevels.Halve(source.Width, source.Height);
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var from = new SKBitmap(info);
        Marshal.Copy(source.Pixels, 0, from.GetPixels(), source.Pixels.Length);

        using var to = from.Resize(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul),
            SKFilterQuality.High);
        if (to is null) return null;

        var pixels = new byte[width * height * 4];
        Marshal.Copy(to.GetPixels(), pixels, 0, pixels.Length);
        return new RasterSurface(width, height, pixels);
    }
}
