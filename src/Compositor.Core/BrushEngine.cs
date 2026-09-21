using Compositor.Core.Selection;

namespace Compositor.Core;

/// <summary>
/// Software brush engine, port of upstream BrushStroke.swift's dab pipeline:
/// pre-rendered tip stamped at even spacing along the path (with a remainder
/// carried across segments), dabs accumulate into a coverage mask using
/// screen (soft) or max (hard) so overlapping dabs NEVER exceed full coverage,
/// and the mask paints through the stroke color at the stroke's opacity —
/// that cap is what keeps overlapping stamps from darkening, Photoshop-style.
/// Painting always recomputes from a pre-stroke snapshot, so incremental
/// live-feedback repaints are idempotent with the final result.
/// </summary>
public sealed class StrokeCoverage
{
    /// <summary>Soft-tip falloff, exact upstream port: normalized Gaussian,
    /// k = 2.5, 1 at the hardness edge to 0 at the rim.</summary>
    public static double Falloff(double t)
    {
        const double k = 2.5;
        var e = Math.Exp(-k * t * t);
        return Math.Max(0d, (e - Math.Exp(-k)) / (1d - Math.Exp(-k)));
    }

    /// <summary>Upstream spacingFraction: hard tips stamp denser than soft ones.</summary>
    public static double SpacingFraction(double hardness) => hardness >= 1 ? 0.015 : 0.025;

    private readonly int _surfaceWidth;
    private readonly int _surfaceHeight;
    private readonly BrushSettings _settings;
    private readonly SelectionClip? _clip;
    private readonly byte[] _coverage;
    private readonly byte[] _tip;
    private readonly int _tipSize;
    private readonly float _tipRadius;
    private readonly float _spacing;
    private (float X, float Y)? _last;
    private float _distanceToNext;
    private (int X, int Y, int Width, int Height) _dirty = (0, 0, 0, 0);

    public StrokeCoverage(int surfaceWidth, int surfaceHeight, BrushSettings settings, SelectionClip? clip = null)
    {
        settings.Validate();
        if (surfaceWidth < 1 || surfaceHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceWidth));
        }
        _surfaceWidth = surfaceWidth;
        _surfaceHeight = surfaceHeight;
        _settings = settings;
        _clip = clip;
        _coverage = new byte[surfaceWidth * surfaceHeight];
        _spacing = (float)Math.Max(0.25, settings.Diameter * SpacingFraction(settings.Hardness));

        // Pre-rendered tip (upstream stamp): only for soft brushes — a hard tip
        // is a plain disc. Tip covers the full diameter box.
        var radius = settings.Radius;
        _tipRadius = radius;
        var n = Math.Max(1, (int)MathF.Ceiling(settings.Diameter));
        _tipSize = n;
        _tip = new byte[n * n];
        if (settings.Hardness < 1f)
        {
            var center = (n - 1) / 2f;
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = MathF.Sqrt((dx * dx) + (dy * dy));
                    if (dist >= radius)
                    {
                        continue;
                    }
                    var u = (dist / radius - settings.Hardness) / (1f - settings.Hardness);
                    _tip[(y * n) + x] = (byte)Math.Clamp(u <= 0f ? 255d : Math.Round(Falloff(u) * 255d), 0d, 255d);
                }
            }
        }
    }

    public BrushSettings Settings => _settings;

    /// <summary>Axis-aligned rect of touched pixels, clamped to the surface.</summary>
    public (int X, int Y, int Width, int Height) DirtyRect => _dirty;

    /// <summary>True when at least one dab landed inside the surface.</summary>
    public bool IsDirty => _dirty.Width > 0;

    /// <summary>Coverage value for tests/diagnostics (0..255).</summary>
    public byte CoverageAt(int x, int y) => _coverage[(y * _surfaceWidth) + x];

    /// <summary>
    /// Walks the pointer to the next position, laying evenly spaced dabs
    /// (upstream walk(to:)): the first point dabs immediately; afterwards dabs
    /// land every <see cref="_spacing"/> px and the fractional remainder of the
    /// last segment carries into the next, so spacing is uniform across the
    /// whole stroke regardless of event granularity.
    /// </summary>
    public void WalkTo(float x, float y)
    {
        if (_last is not { } previous)
        {
            Dab(x, y);
            _distanceToNext = _spacing;
            _last = (x, y);
            return;
        }

        var dx = x - previous.X;
        var dy = y - previous.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length > 0f)
        {
            var distance = _distanceToNext;
            while (distance <= length)
            {
                Dab(previous.X + (dx * distance / length), previous.Y + (dy * distance / length));
                distance += _spacing;
            }
            _distanceToNext = distance - length;
        }
        _last = (x, y);
    }

    /// <summary>Stamps the tip into the coverage mask: screen blend for soft
    /// tips (1-(1-a)(1-b), upstream .screen), max for hard ones (.lighten).</summary>
    private void Dab(float cx, float cy)
    {
        var half = _tipSize / 2f;
        var x0 = (int)MathF.Floor(cx - half);
        var y0 = (int)MathF.Floor(cy - half);
        var soft = _settings.Hardness < 1f;

        for (var ty = 0; ty < _tipSize; ty++)
        {
            var sy = y0 + ty;
            if (sy < 0 || sy >= _surfaceHeight)
            {
                continue;
            }
            for (var tx = 0; tx < _tipSize; tx++)
            {
                var tipAlpha = _tip[(ty * _tipSize) + tx];
                if (!soft)
                {
                    // Hard tip: binary disc from tip geometry (no pre-render).
                    var dxp = (x0 + tx) + 0.5f - cx;
                    var dyp = sy + 0.5f - cy;
                    if ((dxp * dxp) + (dyp * dyp) > _tipRadius * _tipRadius)
                    {
                        continue;
                    }
                    tipAlpha = 255;
                }
                if (tipAlpha == 0)
                {
                    continue;
                }
                var sx = x0 + tx;
                if (sx < 0 || sx >= _surfaceWidth)
                {
                    continue;
                }
                var i = (sy * _surfaceWidth) + sx;
                var old = _coverage[i];
                var next = soft
                    ? old + tipAlpha - ((old * tipAlpha) / 255)
                    : Math.Max(old, tipAlpha);
                _coverage[i] = (byte)Math.Min(255, next);
            }
        }
        TrackDirty(x0, y0, x0 + _tipSize, y0 + _tipSize);
    }

    private void TrackDirty(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(_surfaceWidth, x1);
        y1 = Math.Min(_surfaceHeight, y1);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }
        if (_dirty.Width == 0)
        {
            _dirty = (x0, y0, x1 - x0, y1 - y0);
            return;
        }
        var nx0 = Math.Min(_dirty.X, x0);
        var ny0 = Math.Min(_dirty.Y, y0);
        var nx1 = Math.Max(_dirty.X + _dirty.Width, x1);
        var ny1 = Math.Max(_dirty.Y + _dirty.Height, y1);
        _dirty = (nx0, ny0, nx1 - nx0, ny1 - ny0);
    }

    /// <summary>
    /// Paints the covered region onto the surface, recomputing every pixel
    /// from the PRE-STROKE snapshot (idempotent under repeated calls while
    /// coverage only grows). Selection factor multiplies the applied alpha;
    /// erasing scales the layer's alpha down instead of painting color.
    /// </summary>
    public void PaintRegion(RasterSurface surface, byte[] beforeFull)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(beforeFull);
        if (beforeFull.Length != surface.Pixels.Length)
        {
            throw new ArgumentException("Snapshot size mismatch.", nameof(beforeFull));
        }
        if (_dirty.Width == 0)
        {
            return;
        }
        var opacity = _settings.Opacity;
        var paintedAny = false;
        for (var y = _dirty.Y; y < _dirty.Y + _dirty.Height; y++)
        {
            for (var x = _dirty.X; x < _dirty.X + _dirty.Width; x++)
            {
                var c = _coverage[(y * _surfaceWidth) + x];
                if (c == 0)
                {
                    continue;
                }
                var alpha = (c / 255f) * opacity;
                if (_clip is { } clip)
                {
                    var factor = clip.FactorAt(x, y);
                    if (factor <= 0f)
                    {
                        continue;
                    }
                    alpha *= factor;
                }
                if (alpha <= 0f)
                {
                    continue;
                }
                var i = ((y * _surfaceWidth) + x) * 4;
                var beforeA = beforeFull[i + 3] / 255f;
                if (_settings.Erasing)
                {
                    surface.Pixels[i + 3] = ToByte((beforeA * (1f - alpha)) * 255f);
                }
                else
                {
                    var srcA = alpha * (_settings.A / 255f);
                    var outA = srcA + (beforeA * (1f - srcA));
                    if (outA <= 0f)
                    {
                        continue;
                    }
                    surface.Pixels[i] = ToByte(((_settings.R / 255f * srcA) + (beforeFull[i] / 255f * beforeA * (1f - srcA))) / outA * 255f);
                    surface.Pixels[i + 1] = ToByte(((_settings.G / 255f * srcA) + (beforeFull[i + 1] / 255f * beforeA * (1f - srcA))) / outA * 255f);
                    surface.Pixels[i + 2] = ToByte(((_settings.B / 255f * srcA) + (beforeFull[i + 2] / 255f * beforeA * (1f - srcA))) / outA * 255f);
                    surface.Pixels[i + 3] = ToByte(outA * 255f);
                }
                paintedAny = true;
            }
        }
        if (paintedAny)
        {
            surface.MarkDirty();
        }
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(v + 0.5f, 0f, 255f);
}
