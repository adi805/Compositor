namespace Compositor.Core;

/// <summary>
/// An editor brush stroke as an undoable command, with live-feedback support.
/// The editor paints incrementally while the pointer moves; at stroke end it
/// hands us the full before-snapshot and files the command as already applied,
/// so UndoHistory.Record() stores it without painting a second time
/// (alpha compositing is not idempotent: re-applying would darken the stroke).
/// Undo/redo restore exact region bytes, and repeated redo stays idempotent.
/// </summary>
public sealed class StrokeCommand : IUndoCommand
{
    private readonly RasterSurface _surface;
    private readonly IReadOnlyList<(float X, float Y)> _path;
    private readonly float _radius;
    private readonly byte _r, _g, _b, _a;
    private readonly float _opacity;
    private readonly (int X, int Y, int Width, int Height) _bounds;
    private readonly byte[] _before;
    private byte[]? _after;

    /// <param name="beforeFullSnapshot">
    /// Full-surface pixel buffer as it was BEFORE the stroke was painted
    /// (the editor clones it at stroke start; capturing the full buffer is
    /// required because stroke bounds are unknown until the pointer is up).
    /// Only the stroke's affected region is retained.
    /// </param>
    /// <param name="alreadyApplied">
    /// True when the stroke pixels are already on the surface (live feedback);
    /// the current region is captured as the redo state. False defers the
    /// first Redo() call to apply the stroke, for deferred-execution flows.
    /// </param>
    public StrokeCommand(
        RasterSurface surface,
        IReadOnlyList<(float X, float Y)> path,
        float radius,
        byte r, byte g, byte b, byte a,
        float opacity,
        byte[] beforeFullSnapshot,
        bool alreadyApplied)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _path = path ?? throw new ArgumentNullException(nameof(path));
        ArgumentNullException.ThrowIfNull(beforeFullSnapshot);
        if (_path.Count == 0)
        {
            throw new ArgumentException("Stroke path is empty.", nameof(path));
        }

        if (beforeFullSnapshot.Length != surface.Pixels.Length)
        {
            throw new ArgumentException(
                $"Before-snapshot is {beforeFullSnapshot.Length} bytes; " +
                $"expected {surface.Pixels.Length}.");
        }

        _radius = radius;
        _r = r;
        _g = g;
        _b = b;
        _a = a;
        _opacity = opacity;
        _bounds = BrushStroke.Bounds(path, radius, surface.Width, surface.Height);
        _before = ExtractRegion(beforeFullSnapshot);
        if (alreadyApplied)
        {
            _after = Capture(_bounds);
        }
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
            _surface.MarkDirty();
        }
    }

    public void Undo()
    {
        Restore(_before);
        _surface.MarkDirty();
    }

    private byte[] ExtractRegion(byte[] full)
    {
        if (_bounds.Width == 0 || _bounds.Height == 0)
        {
            return [];
        }

        var region = new byte[_bounds.Width * _bounds.Height * 4];
        for (var y = 0; y < _bounds.Height; y++)
        {
            Array.Copy(
                full,
                (((_bounds.Y + y) * _surface.Width) + _bounds.X) * 4,
                region,
                y * _bounds.Width * 4,
                _bounds.Width * 4);
        }

        return region;
    }

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
