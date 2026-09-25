namespace Compositor.Core.Rendering;

/// <summary>
/// A rectangle in layer-grid pixels, in the same coordinates upstream's <c>CGRect</c> uses. Core carries its
/// own type so the tiling math stays free of a graphics dependency.
/// </summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public static readonly PixelRect Empty = new(0, 0, 0, 0);

    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Grows the rectangle on every side. Upstream spells this <c>insetBy(dx: -margin, dy: -margin)</c>; the
    /// intent is "the pixels a change of mine can reach", so it reads as a grow here.
    /// </summary>
    public PixelRect Grow(double amount) => new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);

    public PixelRect Intersect(PixelRect other)
    {
        var minX = Math.Max(MinX, other.MinX);
        var minY = Math.Max(MinY, other.MinY);
        var maxX = Math.Min(MaxX, other.MaxX);
        var maxY = Math.Min(MaxY, other.MaxY);
        if (maxX <= minX || maxY <= minY) return Empty;
        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Moves the rectangle. Upstream spells this <c>offsetBy(dx:dy:)</c> on CGRect.</summary>
    public PixelRect OffsetBy(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public PixelRect Union(PixelRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        var minX = Math.Min(MinX, other.MinX);
        var minY = Math.Min(MinY, other.MinY);
        var maxX = Math.Max(MaxX, other.MaxX);
        var maxY = Math.Max(MaxY, other.MaxY);
        return new PixelRect(minX, minY, maxX - minX, maxY - minY);
    }

    public bool Intersects(PixelRect other)
        => MinX < other.MaxX && other.MinX < MaxX && MinY < other.MaxY && other.MinY < MaxY;

    public bool Contains(PixelRect other)
        => other.MinX >= MinX && other.MaxX <= MaxX && other.MinY >= MinY && other.MaxY <= MaxY;
}
