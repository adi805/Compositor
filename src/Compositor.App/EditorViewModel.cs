using System.Collections.ObjectModel;
using System.ComponentModel;
using Compositor.Core;
using Compositor.Core.Imaging;
using Compositor.Core.Project;

namespace Compositor.App;

/// <summary>
/// Editor view-model: binds a Core Document to the layers panel and canvas.
/// Pure INPC, no Avalonia types, so it is testable without a UI platform.
/// </summary>
public sealed class EditorViewModel : INotifyPropertyChanged
{
    public Document Doc { get; }

    /// <summary>Rows top-first; the document list is bottom-first.</summary>
    public ObservableCollection<LayerRow> Rows { get; } = new();

    private LayerRow? _selected;
    public LayerRow? Selected
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value))
            {
                return;
            }

            if (_selected is not null)
            {
                _selected.IsActive = false;
            }

            _selected = value;
            if (_selected is not null)
            {
                _selected.IsActive = true;
                Doc.ActiveLayerId = _selected.Id;
            }

            OnPropertyChanged(nameof(Selected));
        }
    }

    public string Title => $"{Doc.Name} - {Doc.Width}x{Doc.Height}";

    /// <summary>Document-level undo stack. Every document mutation goes through here.</summary>
    public UndoHistory History { get; } = new();

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    /// <summary>
    /// Raised whenever rendered document content changed (stroke, undo/redo,
    /// layer stack ops) so the canvas can refresh its pixel previews.
    /// </summary>
    public event Action? DocumentChanged;

    private float _brushRadius = 24f;
    /// <summary>Brush radius in document pixels, clamped to 1..256.</summary>
    public float BrushRadius
    {
        get => _brushRadius;
        set
        {
            var clamped = Math.Clamp(value, 1f, 256f);
            if (Math.Abs(_brushRadius - clamped) < 0.001f)
            {
                return;
            }

            _brushRadius = clamped;
            OnPropertyChanged(nameof(BrushRadius));
        }
    }

    private byte _brushR = 20, _brushG = 20, _brushB = 20;
    public byte BrushR
    {
        get => _brushR;
        set { if (_brushR == value) return; _brushR = value; OnPropertyChanged(nameof(BrushR)); }
    }

    public byte BrushG
    {
        get => _brushG;
        set { if (_brushG == value) return; _brushG = value; OnPropertyChanged(nameof(BrushG)); }
    }

    public byte BrushB
    {
        get => _brushB;
        set { if (_brushB == value) return; _brushB = value; OnPropertyChanged(nameof(BrushB)); }
    }

    public void SetBrushColor(byte r, byte g, byte b)
    {
        BrushR = r;
        BrushG = g;
        BrushB = b;
    }

    private bool _isStrokeActive;
    public bool IsStrokeActive
    {
        get => _isStrokeActive;
        private set { if (_isStrokeActive == value) return; _isStrokeActive = value; OnPropertyChanged(nameof(IsStrokeActive)); }
    }

    private List<(float X, float Y)>? _strokePath;
    private RasterSurface? _strokeSurface;
    private byte[]? _strokeBefore;

    /// <summary>The layer strokes land on; null when no active layer matches.</summary>
    public Layer? ActiveLayer =>
        Doc.Layers.FirstOrDefault(l => l.Id == Doc.ActiveLayerId);

    /// <summary>
    /// Starts a stroke on the active layer in DOCUMENT coordinates.
    /// Materializes the layer's pixel surface if it is still blank.
    /// Returns false when there is nothing to paint on (no active layer,
    /// locked layer) or a stroke is already in progress.
    /// </summary>
    public bool BeginStroke(float docX, float docY)
    {
        if (IsStrokeActive)
        {
            return false;
        }

        var layer = ActiveLayer;
        if (layer is null || layer.IsLocked)
        {
            return false;
        }

        _strokeSurface = layer.Pixels ??= new RasterSurface(Doc.Width, Doc.Height);
        _strokePath = [(docX, docY)];
        _strokeBefore = (byte[])_strokeSurface.Pixels.Clone();
        IsStrokeActive = true;
        return true;
    }

    /// <summary>
    /// Extends the in-progress stroke to a new document point, live-painting
    /// the connecting segment for immediate feedback. Undo is recorded once
    /// per whole stroke, at <see cref="EndStroke"/>.
    /// </summary>
    public void ContinueStroke(float docX, float docY)
    {
        if (!IsStrokeActive || _strokePath is null || _strokeSurface is null)
        {
            return;
        }

        var prev = _strokePath[^1];
        _strokePath.Add((docX, docY));
        BrushStroke.Apply(
            _strokeSurface,
            [(prev.X, prev.Y), (docX, docY)],
            _brushRadius, _brushR, _brushG, _brushB, 255, 1f);
    }

    /// <summary>
    /// Files the finished stroke into the undo history (already applied via
    /// live feedback) and notifies the canvas. Returns false when no stroke
    /// was in progress.
    /// </summary>
    public bool EndStroke()
    {
        if (!IsStrokeActive || _strokePath is null || _strokeBefore is null || _strokeSurface is null)
        {
            return false;
        }

        PaintDotIfNeeded(_strokePath, _strokeSurface);
        var cmd = new StrokeCommand(
            _strokeSurface, _strokePath, _brushRadius,
            _brushR, _brushG, _brushB, 255, 1f,
            _strokeBefore, alreadyApplied: true);
        History.Record(cmd);

        _strokePath = null;
        _strokeBefore = null;
        _strokeSurface = null;
        IsStrokeActive = false;
        OnHistoryChanged();
        RaiseDocumentChanged();
        return true;
    }

    /// <summary>
    /// Paints a click-dot: a single-point path never went through the
    /// live-feedback segment painting, so the stamp happens here.
    /// </summary>
    private void PaintDotIfNeeded(List<(float X, float Y)> path, RasterSurface surface)
    {
        if (path.Count == 1)
        {
            BrushStroke.Apply(
                surface, path, _brushRadius, _brushR, _brushG, _brushB, 255, 1f);
        }
    }

    public void Undo()
    {
        History.Undo();
        OnHistoryChanged();
        RaiseDocumentChanged();
    }

    public void Redo()
    {
        History.Redo();
        OnHistoryChanged();
        RaiseDocumentChanged();
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public void RaiseDocumentChanged() => DocumentChanged?.Invoke();

    public EditorViewModel(Document doc)
    {
        Doc = doc ?? throw new ArgumentNullException(nameof(doc));
        RebuildRows();
        var active = doc.ActiveLayerId;
        Selected = Rows.FirstOrDefault(r => r.Id == active) ?? Rows.FirstOrDefault();
    }

    public EditorViewModel() : this(NewDocument()) { }

    private static Document NewDocument()
    {
        var doc = new Document(1280, 720) { Name = "Untitled" };
        doc.AddLayer(new Layer("Background"));
        return doc;
    }

    public void AddLayer()
    {
        var layer = new Layer($"Layer {Doc.Layers.Count + 1}");
        Doc.AddLayer(layer);
        var row = new LayerRow(layer);
        // Newest layer is topmost: insert at the top of the reversed view.
        Rows.Insert(0, row);
        Selected = row;
        OnPropertyChanged(nameof(Title));
        RaiseDocumentChanged();
    }

    public void DeleteSelected()
    {
        if (_selected is null)
        {
            return;
        }

        var index = Doc.Layers.IndexOf(_selected.Layer);
        if (index < 0)
        {
            return;
        }

        Doc.Layers.RemoveAt(index);
        Rows.Remove(_selected);
        Selected = Rows.FirstOrDefault();
        OnPropertyChanged(nameof(Title));
        RaiseDocumentChanged();
    }

    /// <summary>Moves the selected layer one step toward the top of the stack.</summary>
    public void MoveSelectedUp() => MoveSelected(+1);

    /// <summary>Moves the selected layer one step toward the bottom of the stack.</summary>
    public void MoveSelectedDown() => MoveSelected(-1);

    private void MoveSelected(int docIndexDelta)
    {
        if (_selected is null)
        {
            return;
        }

        // Capture before rebuilding: clearing Rows can reset the ListBox
        // selection, which would push Selected = null back through binding.
        var selectedId = _selected.Id;
        var index = Doc.Layers.IndexOf(_selected.Layer);
        var target = index + docIndexDelta;
        if (index < 0 || target < 0 || target >= Doc.Layers.Count)
        {
            return;
        }

        Doc.Layers.RemoveAt(index);
        Doc.Layers.Insert(target, _selected.Layer);
        RebuildRows();
        Selected = Rows.FirstOrDefault(r => r.Id == selectedId);
        RaiseDocumentChanged();
    }

    public static void ToggleVisibility(LayerRow row) => row.IsVisible = !row.IsVisible;

    private void RebuildRows()
    {
        Rows.Clear();
        for (var i = Doc.Layers.Count - 1; i >= 0; i--)
        {
            Rows.Add(new LayerRow(Doc.Layers[i]));
        }
    }

    // ---------------------------------------------------------------------
    // View transform (zoom/pan). ViewScale 1.0 = fit-to-viewport.
    // ---------------------------------------------------------------------

    public const double MinViewScale = 0.05;
    public const double MaxViewScale = 32.0;

    /// <summary>Raised when the view transform changed so the canvas re-renders.</summary>
    public event Action? ViewChanged;

    private double _viewScale = 1.0;
    public double ViewScale
    {
        get => _viewScale;
        set
        {
            var clamped = Math.Clamp(value, MinViewScale, MaxViewScale);
            if (Math.Abs(_viewScale - clamped) < 0.0001)
            {
                return;
            }

            _viewScale = clamped;
            OnPropertyChanged(nameof(ViewScale));
            ViewChanged?.Invoke();
        }
    }

    private double _viewPanX;
    private double _viewPanY;

    public double ViewPanX
    {
        get => _viewPanX;
        set
        {
            if (Math.Abs(_viewPanX - value) < 0.0001)
            {
                return;
            }

            _viewPanX = value;
            ViewChanged?.Invoke();
        }
    }

    public double ViewPanY
    {
        get => _viewPanY;
        set
        {
            if (Math.Abs(_viewPanY - value) < 0.0001)
            {
                return;
            }

            _viewPanY = value;
            ViewChanged?.Invoke();
        }
    }

    public void PanBy(double dx, double dy)
    {
        ViewPanX += dx;
        ViewPanY += dy;
    }

    public void ResetView()
    {
        ViewScale = 1.0;
        ViewPanX = 0;
        ViewPanY = 0;
    }

    /// <summary>Document canvas rectangle in control coordinates (fit * scale + pan).</summary>
    public (double X, double Y, double W, double H) CanvasRect(double viewportW, double viewportH)
    {
        if (viewportW <= 0 || viewportH <= 0 || Doc.Width <= 0 || Doc.Height <= 0)
        {
            return default;
        }

        var fit = Math.Min(viewportW / Doc.Width, viewportH / Doc.Height);
        var scale = fit * _viewScale;
        var w = Doc.Width * scale;
        var h = Doc.Height * scale;
        return ((viewportW - w) / 2 + _viewPanX, (viewportH - h) / 2 + _viewPanY, w, h);
    }

    /// <summary>Control-space point to document coordinates; null when outside the canvas.</summary>
    public (float X, float Y)? ScreenToDoc(double sx, double sy, double viewportW, double viewportH)
    {
        var r = CanvasRect(viewportW, viewportH);
        if (r.W <= 0 || sx < r.X || sy < r.Y || sx > r.X + r.W || sy > r.Y + r.H)
        {
            return null;
        }

        return ((float)((sx - r.X) * Doc.Width / r.W), (float)((sy - r.Y) * Doc.Height / r.H));
    }

    /// <summary>Zooms by a factor keeping the document point under the cursor anchored.</summary>
    public void ZoomAt(double sx, double sy, double viewportW, double viewportH, double factor)
    {
        var r = CanvasRect(viewportW, viewportH);
        if (r.W <= 0 || r.H <= 0 || factor <= 0)
        {
            return;
        }

        var docU = (sx - r.X) / r.W * Doc.Width;
        var docV = (sy - r.Y) / r.H * Doc.Height;

        ViewScale = Math.Clamp(_viewScale * factor, MinViewScale, MaxViewScale);

        var fit = Math.Min(viewportW / Doc.Width, viewportH / Doc.Height);
        var scale = fit * _viewScale;
        var w = Doc.Width * scale;
        var h = Doc.Height * scale;
        _viewPanX = sx - (docU * scale) - ((viewportW - w) / 2);
        _viewPanY = sy - (docV * scale) - ((viewportH - h) / 2);
        ViewChanged?.Invoke();
    }

    // ---------------------------------------------------------------------
    // File operations. Path-based (no dialogs) so they stay headless-testable;
    // MainWindow pickers only collect paths and delegate here.
    // ---------------------------------------------------------------------

    private string? _currentFilePath;
    public string? CurrentFilePath => _currentFilePath;

    public void SaveProject(string path)
    {
        ProjectStore.Save(Doc, path);
        _currentFilePath = path;
    }

    public void ExportPng(string path)
    {
        var (_, _, rgba) = Flatten.ToRgba(Doc);
        using var output = File.Create(path);
        Png.Encode(output, Doc.Width, Doc.Height, rgba);
    }

    public static EditorViewModel LoadProject(string path)
    {
        var doc = ProjectStore.Load(path);
        return new EditorViewModel(doc) { _currentFilePath = path };
    }

    /// <summary>Imports a PNG file as a new topmost layer, clipped top-left to the canvas.</summary>
    public Layer ImportImagePng(string path, string? name = null)
    {
        using var input = File.OpenRead(path);
        var (w, h, rgba) = Png.Decode(input);
        return ImportRgba(name ?? Path.GetFileNameWithoutExtension(path), w, h, rgba);
    }

    /// <summary>Imports raw straight-alpha RGBA pixels as a new topmost layer, clipped top-left.</summary>
    public Layer ImportRgba(string name, int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
        {
            throw new ArgumentException("Invalid image dimensions or buffer length.");
        }

        var surface = new RasterSurface(Doc.Width, Doc.Height);
        var copyW = Math.Min(width, Doc.Width) * 4;
        var copyH = Math.Min(height, Doc.Height);
        for (var y = 0; y < copyH; y++)
        {
            Array.Copy(rgba, y * width * 4, surface.Pixels, y * Doc.Width * 4, copyW);
        }

        surface.MarkDirty();
        var layer = new Layer(name) { Pixels = surface };
        Doc.AddLayer(layer);
        Rows.Insert(0, new LayerRow(layer));
        Selected = Rows[0];
        OnPropertyChanged(nameof(Title));
        RaiseDocumentChanged();
        return layer;
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
