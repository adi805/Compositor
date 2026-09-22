namespace Compositor.Core.Commands;

/// <summary>
/// Undo for pixel edits applied outside the stroke pipeline (gradient, shape,
/// blur, smudge, clone): the editor applies the pixels, hands us the full
/// before-buffer plus the affected bounds, and we capture the after-state at
/// construction. Undo/redo swap exact region bytes.
/// </summary>
public sealed class RegionCommand : IUndoCommand
{
    private readonly RasterSurface _surface;
    private readonly (int X, int Y, int Width, int Height) _bounds;
    private readonly byte[] _before;
    private readonly byte[] _after;

    public RegionCommand(
        RasterSurface surface,
        byte[] beforeFullSnapshot,
        (int X, int Y, int Width, int Height) bounds)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        ArgumentNullException.ThrowIfNull(beforeFullSnapshot);
        if (beforeFullSnapshot.Length != surface.Pixels.Length)
        {
            throw new ArgumentException("Snapshot size mismatch.", nameof(beforeFullSnapshot));
        }
        _bounds = Clamp(bounds, surface);
        _before = ExtractRegion(beforeFullSnapshot);
        _after = Capture(_bounds);
        surface.MarkDirty();
    }

    public void Undo()
    {
        Restore(_before);
        _surface.MarkDirty();
    }

    public void Redo()
    {
        Restore(_after);
        _surface.MarkDirty();
    }

    private static (int X, int Y, int Width, int Height) Clamp(
        (int X, int Y, int Width, int Height) b, RasterSurface surface)
    {
        var x0 = Math.Clamp(b.X, 0, Math.Max(0, surface.Width - 1));
        var y0 = Math.Clamp(b.Y, 0, Math.Max(0, surface.Height - 1));
        var x1 = Math.Clamp(b.X + b.Width, 0, surface.Width);
        var y1 = Math.Clamp(b.Y + b.Height, 0, surface.Height);
        return x1 <= x0 || y1 <= y0 ? (0, 0, 0, 0) : (x0, y0, x1 - x0, y1 - y0);
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

    private byte[] Capture((int X, int Y, int Width, int Height) b) => ExtractRegion(_surface.Pixels);

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
