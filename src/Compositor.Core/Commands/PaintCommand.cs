namespace Compositor.Core;

/// <summary>
/// A brush stroke as an undoable command. The constructor snapshots the
/// affected region; Redo() applies the stroke and snapshots the result, so
/// undo/redo restore exact pixels and repeated redo stays idempotent.
/// </summary>
public sealed class PaintCommand : IUndoCommand
{
    private readonly RasterSurface _surface;
    private readonly IReadOnlyList<(float X, float Y)> _path;
    private readonly float _radius;
    private readonly byte _r, _g, _b, _a;
    private readonly float _opacity;
    private readonly (int X, int Y, int Width, int Height) _bounds;
    private readonly byte[] _before;
    private byte[]? _after;

    public PaintCommand(
        RasterSurface surface,
        IReadOnlyList<(float X, float Y)> path,
        float radius,
        byte r, byte g, byte b, byte a,
        float opacity)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _path = path ?? throw new ArgumentNullException(nameof(path));
        if (_path.Count == 0)
        {
            throw new ArgumentException("Stroke path is empty.", nameof(path));
        }

        _radius = radius;
        _r = r;
        _g = g;
        _b = b;
        _a = a;
        _opacity = opacity;
        _bounds = BrushStroke.Bounds(path, radius, surface.Width, surface.Height);
        _before = Capture(_bounds);
    }

    public void Redo()
    {
        if (_after is null)
        {
            BrushStroke.Apply(_surface, _path, _radius, _r, _g, _b, _a, _opacity);
            _after = Capture(_bounds);
        }
        else
        {
            Restore(_after);
        }
    }

    public void Undo() => Restore(_before);

    private byte[] Capture((int X, int Y, int Width, int Height) b)
    {
        if (b.Width == 0 || b.Height == 0)
        {
            return [];
        }

        var bytes = new byte[b.Width * b.Height * 4];
        for (var y = 0; y < b.Height; y++)
        {
            Array.Copy(
                _surface.Pixels,
                (((b.Y + y) * _surface.Width) + b.X) * 4,
                bytes,
                y * b.Width * 4,
                b.Width * 4);
        }

        return bytes;
    }

    private void Restore(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        for (var y = 0; y < _bounds.Height; y++)
        {
            Array.Copy(
                bytes,
                y * _bounds.Width * 4,
                _surface.Pixels,
                (((_bounds.Y + y) * _surface.Width) + _bounds.X) * 4,
                _bounds.Width * 4);
        }
    }
}
