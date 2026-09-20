using System.Collections.ObjectModel;
using System.ComponentModel;
using Compositor.Core;

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

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
