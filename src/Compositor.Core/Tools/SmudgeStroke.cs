namespace Compositor.Core.Tools;

/// <summary>
/// Smudge tool, upstream SmudgeLiquify.swift (smudge mode): the brush carries
/// a (2r+1)² premultiplied RGBA square picked up when the stroke starts; each
/// dab lerps the pixels under it toward what the brush carries (weight =
/// smoothstep from the hardness edge to the rim, strength = opacity), and the
/// brush picks up some of what it just left ("keep" = strength). Dab spacing
/// is max(1, diameter × 8%). Works on a premultiplied working copy, exactly
/// like upstream's premultiplied context. Liquify's forward-warp push is a
/// separate mode and is NOT ported here (tracked in PARITY.md).
/// </summary>
public sealed class SmudgeStroke
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _working;     // premultiplied
    private readonly float[] _carried;    // premultiplied square
    private readonly int _radius;
    private readonly int _side;
    private readonly BrushSettings _settings;
    private readonly float _strength;
    private readonly float _hardness;
    private readonly float _spacing;
    private (float X, float Y)? _last;

    public SmudgeStroke(int surfaceWidth, int surfaceHeight, BrushSettings settings, byte[] surfacePixels)
    {
        settings.Validate();
        if (surfaceWidth < 1 || surfaceHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(surfaceWidth));
        }
        if (surfacePixels.Length != surfaceWidth * surfaceHeight * 4)
        {
            throw new ArgumentException("Pixel buffer size mismatch.", nameof(surfacePixels));
        }
        _width = surfaceWidth;
        _height = surfaceHeight;
        _settings = settings;
        _strength = Math.Clamp(settings.Opacity, 0.01f, 1f);
        _hardness = Math.Clamp(settings.Hardness, 0f, 0.98f);
        var diameter = Math.Max(2, settings.Diameter);
        _spacing = MathF.Max(1f, diameter * 0.08f);
        _radius = (int)MathF.Ceiling(diameter / 2f);
        _side = (2 * _radius) + 1;
        _carried = new float[_side * _side * 4];
        _working = ToPremultiplied(surfacePixels);
    }

    public int Radius => _radius;

    /// <summary>Weight of a dab at relative distance u (0 center, 1 rim):
    /// 1 inside the hardness edge, then smoothstep t²(3−2t) to the rim.</summary>
    public static float Weight(float u, float hardness)
    {
        if (u >= 1f)
        {
            return 0f;
        }
        if (u <= hardness)
        {
            return 1f;
        }
        var t = (1f - u) / (1f - hardness);
        return (t * t) * (3f - (2f * t));
    }

    /// <summary>Starts (or continues) the stroke; the first point picks up.</summary>
    public void WalkTo(float x, float y, byte[] surfacePixels)
    {
        if (_last is not { } from)
        {
            _last = (x, y);
            PickUp(x, y);
            return;
        }
        var dx = x - from.X;
        var dy = y - from.Y;
        var distance = MathF.Sqrt((dx * dx) + (dy * dy));
        if (distance < _spacing)
        {
            return;
        }
        var steps = (int)MathF.Round(distance / _spacing);
        var previous = from;
        for (var step = 1; step <= steps; step++)
        {
            var t = step / (float)steps;
            var next = (from.X + (dx * t), from.Y + (dy * t));
            SmudgeDab(next.Item1, next.Item2);
            previous = next;
        }
        _last = (x, y);
    }

    /// <summary>Writes the working copy back to straight-alpha surface bytes.</summary>
    public void Commit(byte[] surfacePixels)
    {
        ToStraight(_working, surfacePixels);
    }

    private void PickUp(float centerX, float centerY)
    {
        Array.Clear(_carried);
        var cx = (int)MathF.Round(centerX);
        var cy = (int)MathF.Round(centerY);
        for (var dy = -_radius; dy <= _radius; dy++)
        {
            var y = cy + dy;
            if (y < 0 || y >= _height)
            {
                continue;
            }
            for (var dx = -_radius; dx <= _radius; dx++)
            {
                var x = cx + dx;
                if (x < 0 || x >= _width)
                {
                    continue;
                }
                var p = ((y * _width) + x) * 4;
                var c = (((dy + _radius) * _side) + dx + _radius) * 4;
                _carried[c] = _working[p];
                _carried[c + 1] = _working[p + 1];
                _carried[c + 2] = _working[p + 2];
                _carried[c + 3] = _working[p + 3];
            }
        }
    }

    private void SmudgeDab(float centerX, float centerY)
    {
        var cx = (int)MathF.Round(centerX);
        var cy = (int)MathF.Round(centerY);
        var invR = 1f / (_settings.Diameter / 2f);
        for (var dy = -_radius; dy <= _radius; dy++)
        {
            var y = cy + dy;
            if (y < 0 || y >= _height)
            {
                continue;
            }
            for (var dx = -_radius; dx <= _radius; dx++)
            {
                var x = cx + dx;
                if (x < 0 || x >= _width)
                {
                    continue;
                }
                var w = Weight(MathF.Sqrt((dx * dx) + (dy * dy)) * invR, _hardness);
                if (w <= 0f)
                {
                    continue;
                }
                var p = ((y * _width) + x) * 4;
                var c = (((dy + _radius) * _side) + dx + _radius) * 4;
                for (var k = 0; k < 4; k++)
                {
                    var under = _working[p + k];
                    var painted = under + ((_carried[c + k] - under) * w);
                    _working[p + k] = (byte)Math.Clamp((int)MathF.Round(painted), 0, 255);
                    // The brush picks up some of what it just left, more the weaker the smudge.
                    _carried[c + k] = painted + ((_carried[c + k] - painted) * _strength);
                }
            }
        }
    }

    private static byte[] ToPremultiplied(byte[] rgba)
    {
        var result = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var a = rgba[i + 3] / 255f;
            result[i] = (byte)MathF.Round(rgba[i] * a);
            result[i + 1] = (byte)MathF.Round(rgba[i + 1] * a);
            result[i + 2] = (byte)MathF.Round(rgba[i + 2] * a);
            result[i + 3] = rgba[i + 3];
        }
        return result;
    }

    private static void ToStraight(byte[] premul, byte[] rgba)
    {
        for (var i = 0; i < premul.Length; i += 4)
        {
            var a = premul[i + 3] / 255f;
            rgba[i + 3] = premul[i + 3];
            if (a <= 0f)
            {
                rgba[i] = 0;
                rgba[i + 1] = 0;
                rgba[i + 2] = 0;
                continue;
            }
            rgba[i] = (byte)Math.Clamp(MathF.Round(premul[i] / a), 0, 255);
            rgba[i + 1] = (byte)Math.Clamp(MathF.Round(premul[i + 1] / a), 0, 255);
            rgba[i + 2] = (byte)Math.Clamp(MathF.Round(premul[i + 2] / a), 0, 255);
        }
    }
}
