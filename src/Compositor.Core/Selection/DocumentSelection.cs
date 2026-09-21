namespace Compositor.Core.Selection;

public enum SelectionKind
{
    Rectangle,
    Ellipse,
    Polygon,
}

public enum SelectionMode
{
    Replace,
    Add,
    Subtract,
}

/// <summary>
/// A document-space selection, ported from upstream DocumentSelection.swift:
/// null on the document means no selection; an explicit empty selection means
/// later edits must touch NOTHING, never everything. Coverage is a grayscale
/// byte mask at document resolution (255 = selected), antialiased by 2x2
/// supersampling.
/// </summary>
public class DocumentSelection
{
    private readonly List<SelectionShape> _shapes;

    public IReadOnlyList<SelectionShape> Shapes => _shapes;
    public bool IsEmpty { get; }

    protected DocumentSelection(List<SelectionShape> shapes, bool isEmpty)
    {
        _shapes = shapes;
        IsEmpty = isEmpty;
    }

    public static DocumentSelection Empty() => new([], isEmpty: true);

    /// <summary>Builds a selection from one shape.</summary>
    public static DocumentSelection FromShape(SelectionShape shape) => ApplyTo(null, shape);

    /// <summary>Returns a new selection with the shape applied in its mode.</summary>
    public static DocumentSelection ApplyTo(DocumentSelection? existing, SelectionShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var shapes = existing is { IsEmpty: false } ? new List<SelectionShape>(existing._shapes) : [];
        if (shape.Mode == SelectionMode.Replace)
        {
            shapes.Clear();
        }
        if (shape.Mode == SelectionMode.Subtract && shapes.Count == 0)
        {
            return Empty(); // subtracting from nothing stays nothing
        }
        shapes.Add(shape);
        return new DocumentSelection(shapes, isEmpty: false);
    }

    public DocumentSelection Apply(SelectionShape shape) => ApplyTo(this, shape);

    /// <summary>Selects everything (a single full-canvas rectangle).</summary>
    public static DocumentSelection All(int width, int height) =>
        FromShape(SelectionShape.Rectangle(0, 0, width, height, SelectionMode.Replace));

    /// <summary>Inverts coverage within the canvas bounds (upstream Select Invert).</summary>
    public DocumentSelection Inverted(int width, int height)
    {
        var coverage = RenderCoverage(width, height);
        for (var i = 0; i < coverage.Length; i++)
        {
            coverage[i] = (byte)(255 - coverage[i]);
        }
        return FromCoverage(coverage, width, height);
    }

    /// <summary>Renders the full-canvas coverage mask (row-major, w*h bytes).</summary>
    public virtual byte[] RenderCoverage(int width, int height)
    {
        var coverage = new byte[width * height];
        if (IsEmpty)
        {
            return coverage; // explicit empty: all zeros
        }

        foreach (var shape in _shapes)
        {
            var shapeMask = RenderShape(shape, width, height);
            switch (shape.Mode)
            {
                case SelectionMode.Replace or SelectionMode.Add:
                    for (var i = 0; i < coverage.Length; i++)
                    {
                        coverage[i] = (byte)Math.Max(coverage[i], shapeMask[i]);
                    }
                    break;
                case SelectionMode.Subtract:
                    for (var i = 0; i < coverage.Length; i++)
                    {
                        coverage[i] = (byte)Math.Clamp(coverage[i] - shapeMask[i], 0, 255);
                    }
                    break;
            }
        }
        return coverage;
    }

    /// <summary>Crops the coverage to its bounding box for efficient masked ops.</summary>
    public SelectionClip Clip(int width, int height) => ToClip(RenderCoverage(width, height), width, height);

    /// <summary>Builds a selection straight from a rendered mask (magic wand path).</summary>
    public static DocumentSelection FromCoverage(byte[] coverage, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        if (coverage.Length != width * height)
        {
            throw new ArgumentException("Coverage length must equal width*height.", nameof(coverage));
        }
        var clip = ToClip(coverage, width, height);
        return clip.Coverage is null ? Empty() : new CoverageBackedSelection(clip.X, clip.Y, clip.Width, clip.Height, clip.Coverage);
    }

    private static SelectionClip ToClip(byte[] coverage, int width, int height)
    {
        var minX = width; var minY = height; var maxX = -1; var maxY = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (coverage[row + x] > 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < 0)
        {
            return SelectionClip.Empty; // touch nothing
        }

        var w = maxX - minX + 1;
        var h = maxY - minY + 1;
        var local = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            Buffer.BlockCopy(coverage, ((minY + y) * width) + minX, local, y * w, w);
        }
        return new SelectionClip(minX, minY, w, h, local);
    }

    private static byte[] RenderShape(SelectionShape shape, int width, int height)
    {
        var mask = new byte[width * height];
        const int ss = 2; // 2x2 supersampling for antialiased edges
        const int ssArea = ss * ss;

        var (x0, y0, x1, y1) = shape.Bounds();
        var startX = Math.Max(0, (int)MathF.Floor(x0) - 1);
        var startY = Math.Max(0, (int)MathF.Floor(y0) - 1);
        var endX = Math.Min(width - 1, (int)MathF.Ceiling(x1) + 1);
        var endY = Math.Min(height - 1, (int)MathF.Ceiling(y1) + 1);

        for (var y = startY; y <= endY; y++)
        {
            for (var x = startX; x <= endX; x++)
            {
                var hits = 0;
                for (var sy = 0; sy < ss; sy++)
                {
                    for (var sx = 0; sx < ss; sx++)
                    {
                        var px = x + ((sx + 0.5f) / ss);
                        var py = y + ((sy + 0.5f) / ss);
                        if (shape.Contains(px, py))
                        {
                            hits++;
                        }
                    }
                }
                if (hits > 0)
                {
                    mask[(y * width) + x] = (byte)(255 * hits / ssArea);
                }
            }
        }
        return mask;
    }
}

/// <summary>Selection whose coverage comes from a raster (magic wand), not geometry.</summary>
public sealed class CoverageBackedSelection : DocumentSelection
{
    private readonly int _x;
    private readonly int _y;
    private readonly int _w;
    private readonly int _h;
    private readonly byte[] _local;

    internal CoverageBackedSelection(int x, int y, int w, int h, byte[] local)
        : base([], isEmpty: false)
    {
        _x = x;
        _y = y;
        _w = w;
        _h = h;
        _local = local;
    }

    public override byte[] RenderCoverage(int width, int height)
    {
        var result = new byte[width * height];
        for (var y = 0; y < _h; y++)
        {
            var targetY = _y + y;
            if (targetY < 0 || targetY >= height)
            {
                continue;
            }
            for (var x = 0; x < _w; x++)
            {
                var targetX = _x + x;
                if (targetX < 0 || targetX >= width)
                {
                    continue;
                }
                result[(targetY * width) + targetX] = _local[(y * _w) + x];
            }
        }
        return result;
    }
}

/// <summary>One geometric contributor to a selection, with its combine mode.</summary>
public sealed class SelectionShape
{
    public SelectionKind Kind { get; }
    public SelectionMode Mode { get; }
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public IReadOnlyList<(float X, float Y)> Points { get; }

    private SelectionShape(SelectionKind kind, SelectionMode mode,
        float x, float y, float width, float height,
        IReadOnlyList<(float X, float Y)>? points)
    {
        Kind = kind;
        Mode = mode;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Points = points ?? [];
    }

    public static SelectionShape Rectangle(float x, float y, float w, float h, SelectionMode mode) =>
        new(SelectionKind.Rectangle, mode, MathF.Min(x, x + w), MathF.Min(y, y + h), MathF.Abs(w), MathF.Abs(h), null);

    public static SelectionShape Ellipse(float x, float y, float w, float h, SelectionMode mode) =>
        new(SelectionKind.Ellipse, mode, MathF.Min(x, x + w), MathF.Min(y, y + h), MathF.Abs(w), MathF.Abs(h), null);

    public static SelectionShape Lasso(IReadOnlyList<(float X, float Y)> points, SelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < 3)
        {
            throw new ArgumentException("A lasso needs at least 3 points.", nameof(points));
        }
        var minX = float.MaxValue; var minY = float.MaxValue; var maxX = float.MinValue; var maxY = float.MinValue;
        foreach (var (px, py) in points)
        {
            if (px < minX) minX = px;
            if (py < minY) minY = py;
            if (px > maxX) maxX = px;
            if (py > maxY) maxY = py;
        }
        return new SelectionShape(SelectionKind.Polygon, mode, minX, minY, maxX - minX, maxY - minY, points);
    }

    public (float X0, float Y0, float X1, float Y1) Bounds() => (X, Y, X + Width, Y + Height);

    public bool Contains(float px, float py)
    {
        return Kind switch
        {
            SelectionKind.Rectangle => px >= X && px < X + Width && py >= Y && py < Y + Height,
            SelectionKind.Ellipse => InEllipse(px, py),
            SelectionKind.Polygon => InPolygon(px, py),
            _ => false,
        };
    }

    private bool InEllipse(float px, float py)
    {
        if (Width <= 0 || Height <= 0)
        {
            return false;
        }
        var cx = X + (Width / 2f);
        var cy = Y + (Height / 2f);
        var dx = (px - cx) / (Width / 2f);
        var dy = (py - cy) / (Height / 2f);
        return (dx * dx) + (dy * dy) <= 1f;
    }

    /// <summary>Even-odd rule over the closed outline.</summary>
    private bool InPolygon(float px, float py)
    {
        var inside = false;
        var n = Points.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var (xi, yi) = Points[i];
            var (xj, yj) = Points[j];
            var intersects = ((yi > py) != (yj > py)) &&
                (px < (((xj - xi) * (py - yi) / (yj - yi)) + xi));
            if (intersects)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}

/// <summary>
/// Coverage for one region of the document, ready to clip edits (upstream
/// SelectionClip). A null Coverage means "touch nothing".
/// </summary>
public sealed class SelectionClip
{
    public static readonly SelectionClip Empty = new(0, 0, 0, 0, null);

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[]? Coverage { get; }

    public SelectionClip(int x, int y, int width, int height, byte[]? coverage)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Coverage = coverage;
    }

    /// <summary>Blend factor 0..1 for document pixel (x, y); 0 when outside.</summary>
    public float FactorAt(int x, int y)
    {
        if (Coverage is null)
        {
            return 0f;
        }
        var lx = x - X;
        var ly = y - Y;
        if (lx < 0 || ly < 0 || lx >= Width || ly >= Height)
        {
            return 0f;
        }
        return Coverage[(ly * Width) + lx] / 255f;
    }
}
