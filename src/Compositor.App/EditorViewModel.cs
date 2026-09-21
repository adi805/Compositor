using System.Collections.ObjectModel;
using System.ComponentModel;
using Compositor.Core;
using Compositor.Core.Adjustments;
using Compositor.Core.Commands;
using Compositor.Core.Imaging;
using Compositor.Core.Project;
using Compositor.Core.Selection;

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

    private float _brushDiameter = 40f;
    /// <summary>Brush diameter in document pixels (upstream default 40), clamped to 1..512.</summary>
    public float BrushDiameter
    {
        get => _brushDiameter;
        set
        {
            var clamped = Math.Clamp(value, 1f, 512f);
            if (Math.Abs(_brushDiameter - clamped) < 0.001f)
            {
                return;
            }

            _brushDiameter = clamped;
            OnPropertyChanged(nameof(BrushDiameter));
        }
    }

    private float _brushHardness = 1f;
    /// <summary>0 = fully soft (Gaussian falloff to the rim), 1 = hard disc.</summary>
    public float BrushHardness
    {
        get => _brushHardness;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_brushHardness - clamped) < 0.001f)
            {
                return;
            }

            _brushHardness = clamped;
            OnPropertyChanged(nameof(BrushHardness));
        }
    }

    private float _brushOpacity = 1f;
    /// <summary>Stroke opacity cap: overlapping dabs never exceed it.</summary>
    public float BrushOpacity
    {
        get => _brushOpacity;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_brushOpacity - clamped) < 0.001f)
            {
                return;
            }

            _brushOpacity = clamped;
            OnPropertyChanged(nameof(BrushOpacity));
        }
    }

    private bool _isErasing;
    /// <summary>Eraser mode: strokes clear the layer's alpha (upstream erasing).</summary>
    public bool IsErasing
    {
        get => _isErasing;
        set { if (_isErasing == value) return; _isErasing = value; OnPropertyChanged(nameof(IsErasing)); }
    }

    private BrushSettings SnapshotBrushSettings() => new()
    {
        Diameter = _brushDiameter,
        Hardness = _brushHardness,
        R = _brushR,
        G = _brushG,
        B = _brushB,
        A = 255,
        Opacity = _brushOpacity,
        Erasing = _isErasing,
    };

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
    private SelectionClip? _strokeClip;
    private StrokeCoverage? _strokeCoverage;
    private BrushSettings? _strokeSettings;

    /// <summary>Which tool the canvas pointer feeds (brush or a selection kind).</summary>
    public enum EditorTool { Brush, RectangleSelect, EllipseSelect, LassoSelect }

    private EditorTool _tool = EditorTool.Brush;
    public EditorTool Tool
    {
        get => _tool;
        set { if (_tool == value) return; _tool = value; OnPropertyChanged(nameof(Tool)); }
    }

    private bool _isMarqueeActive;
    public bool IsMarqueeActive
    {
        get => _isMarqueeActive;
        private set { if (_isMarqueeActive == value) return; _isMarqueeActive = value; OnPropertyChanged(nameof(IsMarqueeActive)); }
    }

    /// <summary>Live marquee bounds in document coords (rectangle/ellipse draft).</summary>
    public (float X, float Y, float W, float H)? DraftBounds { get; private set; }

    /// <summary>Live lasso outline in document coords (lasso draft).</summary>
    public IReadOnlyList<(float X, float Y)>? DraftPoints { get; private set; }

    private SelectionClipboardData? _clipboard;
    private FloatingSelection? _floating;
    public FloatingSelection? Floating => _floating;

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
        _strokeClip = Doc.Selection is { IsEmpty: false } sel ? sel.Clip(Doc.Width, Doc.Height) : null;
        _strokeSettings = SnapshotBrushSettings();
        _strokeCoverage = new StrokeCoverage(_strokeSurface.Width, _strokeSurface.Height, _strokeSettings, _strokeClip);
        _strokeCoverage.WalkTo(docX, docY); // first dab lands immediately (upstream walk)
        _strokeCoverage.PaintRegion(_strokeSurface, _strokeBefore);
        IsStrokeActive = true;
        return true;
    }

    /// <summary>
    /// Extends the in-progress stroke to a new document point, laying dabs at
    /// even spacing for immediate feedback. Undo is recorded once per whole
    /// stroke, at <see cref="EndStroke"/>.
    /// </summary>
    public void ContinueStroke(float docX, float docY)
    {
        if (!IsStrokeActive || _strokePath is null || _strokeSurface is null
            || _strokeCoverage is null || _strokeBefore is null)
        {
            return;
        }

        _strokePath.Add((docX, docY));
        _strokeCoverage.WalkTo(docX, docY);
        _strokeCoverage.PaintRegion(_strokeSurface, _strokeBefore);
    }

    /// <summary>
    /// Files the finished stroke into the undo history (already applied via
    /// live feedback) and notifies the canvas. Returns false when no stroke
    /// was in progress.
    /// </summary>
    public bool EndStroke()
    {
        if (!IsStrokeActive || _strokePath is null || _strokeBefore is null || _strokeSurface is null
            || _strokeCoverage is null || _strokeSettings is null)
        {
            return false;
        }

        // Final pass from the pre-stroke snapshot (idempotent), then file the
        // command as already applied — see StrokeCommand.
        _strokeCoverage.PaintRegion(_strokeSurface, _strokeBefore);
        var cmd = new StrokeCommand(
            _strokeSurface, _strokePath, _strokeSettings,
            _strokeBefore, alreadyApplied: true, _strokeClip);
        History.Record(cmd);

        _strokePath = null;
        _strokeBefore = null;
        _strokeSurface = null;
        _strokeClip = null;
        _strokeCoverage = null;
        _strokeSettings = null;
        IsStrokeActive = false;
        OnHistoryChanged();
        RaiseDocumentChanged();
        return true;
    }

    // ---- Selection tools (marquee / lasso) and clipboard commands ----

    private (float X, float Y)? _marqueeAnchor;
    private List<(float X, float Y)>? _lassoDraft;

    /// <summary>Starts a marquee/lasso drag in document coordinates.</summary>
    public bool BeginMarquee(float docX, float docY)
    {
        if (IsMarqueeActive || Tool == EditorTool.Brush)
        {
            return false;
        }
        _marqueeAnchor = (docX, docY);
        _lassoDraft = Tool == EditorTool.LassoSelect ? [(docX, docY)] : null;
        DraftPoints = null;
        DraftBounds = (docX, docY, 0f, 0f);
        IsMarqueeActive = true;
        return true;
    }

    /// <summary>Extends the in-progress marquee/lasso to a new document point.</summary>
    public void ContinueMarquee(float docX, float docY)
    {
        if (!IsMarqueeActive || _marqueeAnchor is not { } anchor)
        {
            return;
        }

        if (_lassoDraft is not null)
        {
            _lassoDraft.Add((docX, docY));
            DraftPoints = _lassoDraft.ToArray();
        }
        else
        {
            DraftBounds = (MathF.Min(anchor.X, docX), MathF.Min(anchor.Y, docY),
                MathF.Abs(docX - anchor.X), MathF.Abs(docY - anchor.Y));
        }
    }

    /// <summary>Commits the draft into the document's selection (undo-safe session state).</summary>
    public void EndMarquee()
    {
        if (!IsMarqueeActive)
        {
            return;
        }

        SelectionMode mode = SelectionMode.Replace;
        DocumentSelection? shape = null;
        if (_lassoDraft is { Count: >= 3 } points)
        {
            shape = DocumentSelection.ApplyTo(Doc.Selection, SelectionShape.Lasso(points, mode));
        }
        else if (_marqueeAnchor is { } anchor && DraftBounds is { } b && (b.W >= 1 || b.H >= 1))
        {
            var kind = Tool == EditorTool.EllipseSelect ? SelectionKind.Ellipse : SelectionKind.Rectangle;
            shape = kind == SelectionKind.Ellipse
                ? DocumentSelection.ApplyTo(Doc.Selection, SelectionShape.Ellipse(anchor.X, anchor.Y, b.W, b.H, mode))
                : DocumentSelection.ApplyTo(Doc.Selection, SelectionShape.Rectangle(anchor.X, anchor.Y, b.W, b.H, mode));
        }

        _marqueeAnchor = null;
        _lassoDraft = null;
        DraftBounds = null;
        DraftPoints = null;
        IsMarqueeActive = false;

        if (shape is not null)
        {
            Doc.Selection = shape.IsEmpty ? null : shape;
        }
        RaiseDocumentChanged();
    }

    public void SelectAll()
    {
        if (Doc.Selection is { IsEmpty: false } existing)
        {
            Doc.Selection = DocumentSelection.ApplyTo(existing, SelectionShape.Rectangle(0, 0, Doc.Width, Doc.Height, SelectionMode.Add));
        }
        else
        {
            Doc.Selection = DocumentSelection.All(Doc.Width, Doc.Height);
        }
        RaiseDocumentChanged();
    }

    public void Deselect()
    {
        Doc.Selection = null;
        RaiseDocumentChanged();
    }

    public void InvertSelection()
    {
        Doc.Selection = (Doc.Selection is { IsEmpty: false } existing ? existing : DocumentSelection.Empty())
            .Inverted(Doc.Width, Doc.Height);
        if (Doc.Selection.IsEmpty)
        {
            Doc.Selection = null;
        }
        RaiseDocumentChanged();
    }

    public void CopySelection()
    {
        if (Doc.Selection is not { IsEmpty: false } sel || ActiveLayer?.Pixels is null)
        {
            return;
        }
        _clipboard = SelectionOps.Copy(ActiveLayer, sel, Doc.Width, Doc.Height);
    }

    public void CutSelection()
    {
        if (Doc.Selection is not { IsEmpty: false } sel || ActiveLayer is not { } layer || layer.Pixels is null)
        {
            return;
        }
        _clipboard = SelectionOps.Cut(Doc, layer, sel);
        RaiseDocumentChanged();
    }

    /// <summary>Pastes the clipboard as a floating selection at its origin.</summary>
    public void PasteSelection()
    {
        if (_clipboard is not { } data)
        {
            return;
        }
        _floating = new FloatingSelection((byte[])data.Pixels.Clone(), data.X, data.Y, data.Width, data.Height);
        OnPropertyChanged(nameof(Floating));
        RaiseDocumentChanged();
    }

    public void MoveFloating(int dx, int dy) => _floating?.Move(dx, dy);

    /// <summary>Alpha-over merges the floating pixels into the active layer (one undo step).</summary>
    public void CommitFloating()
    {
        if (_floating is not { } floating || ActiveLayer is not { } layer)
        {
            return;
        }
        History.Push(SelectionOps.CommitFloating(Doc, layer, floating));
        _floating = null;
        OnPropertyChanged(nameof(Floating));
        OnHistoryChanged();
        RaiseDocumentChanged();
    }

    /// <summary>Drops the floating pixels without touching the layer.</summary>
    public void CancelFloating()
    {
        _floating = null;
        OnPropertyChanged(nameof(Floating));
        RaiseDocumentChanged();
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
        var layer = _selected.Layer;
        var index = Doc.Layers.IndexOf(layer);
        var target = index + docIndexDelta;
        if (index < 0 || target < 0 || target >= Doc.Layers.Count)
        {
            return;
        }

        History.Push(new ReorderLayerCommand(Doc, layer, target));
        RebuildRows();
        Selected = Rows.FirstOrDefault(r => r.Id == selectedId);
        OnHistoryChanged();
        RaiseDocumentChanged();
    }

    public static void ToggleVisibility(LayerRow row) => row.IsVisible = !row.IsVisible;

    private void RebuildRows()
    {
        Rows.Clear();
        // Top-first UI order over the hierarchy; groups render as indented rows.
        var entries = LayerHierarchy.Entries(Doc.Layers);
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var row = new LayerRow(entries[i].Layer) { Depth = entries[i].Depth };
            row.LayerChanged += OnRowChanged;
            Rows.Add(row);
        }
    }

    private void OnRowChanged(LayerRow row)
    {
        RaiseDocumentChanged();
    }

    // ---------------------------------------------------------------------
    // Layer power: appearance editing, groups, merge, flip (parity WS5).
    // ---------------------------------------------------------------------

    /// <summary>Appearance editing targets a single selected, non-group layer (upstream canEditAppearance).</summary>
    public bool CanEditAppearance => Selected is { } row && !row.Layer.IsGroup;

    // --- Geometry: canvas size / image size / crop (upstream CanvasResizer/ImageResizer/Crop) ---

    /// <summary>Canvas Size with an anchor 0..8 and optional extension fill. False when rejected.</summary>
    public bool ApplyCanvasSize(int newWidth, int newHeight, int anchor, (byte R, byte G, byte B)? fill)
    {
        CanvasResizeCommand command;
        try
        {
            command = CanvasResizeCommand.AnchorResize(Doc, newWidth, newHeight, anchor, fill);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        PushAndRefresh(command);
        OnPropertyChanged(nameof(Title));
        return true;
    }

    /// <summary>Image Size resample with DPI. False when rejected.</summary>
    public bool ApplyImageSize(int newWidth, int newHeight, double resolution)
    {
        ImageSizeCommand command;
        try
        {
            command = new ImageSizeCommand(Doc, newWidth, newHeight, resolution);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        PushAndRefresh(command);
        OnPropertyChanged(nameof(Title));
        return true;
    }

    /// <summary>Crop to the given document-space rectangle. False when rejected.</summary>
    public bool ApplyCrop(int x, int y, int width, int height)
    {
        CanvasResizeCommand command;
        try
        {
            command = CanvasResizeCommand.Crop(Doc, x, y, width, height);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        PushAndRefresh(command);
        OnPropertyChanged(nameof(Title));
        return true;
    }

    /// <summary>
    /// Pixel bounds of the current selection (union of shape rects/points,
    /// floored and clamped to the canvas) for Crop-to-Selection. Null when none.
    /// </summary>
    public (int X, int Y, int Width, int Height)? SelectionPixelBounds()
    {
        if (Doc.Selection is not { } selection || selection.IsEmpty || selection.Shapes.Count == 0)
        {
            return null;
        }
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        foreach (var shape in selection.Shapes)
        {
            if (shape.Points.Count > 0)
            {
                foreach (var (px, py) in shape.Points)
                {
                    minX = MathF.Min(minX, px);
                    minY = MathF.Min(minY, py);
                    maxX = MathF.Max(maxX, px);
                    maxY = MathF.Max(maxY, py);
                }
                continue;
            }
            minX = MathF.Min(minX, shape.X);
            minY = MathF.Min(minY, shape.Y);
            maxX = MathF.Max(maxX, shape.X + shape.Width);
            maxY = MathF.Max(maxY, shape.Y + shape.Height);
        }
        if (minX > maxX || minY > maxY)
        {
            return null;
        }
        var x0 = Math.Clamp((int)MathF.Floor(minX), 0, Doc.Width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(minY), 0, Doc.Height - 1);
        var x1 = Math.Clamp((int)MathF.Ceiling(maxX), 1, Doc.Width);
        var y1 = Math.Clamp((int)MathF.Ceiling(maxY), 1, Doc.Height);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Crop to the current selection bounds. False when there is no selection.</summary>
    public bool ApplyCropToSelection()
    {
        if (SelectionPixelBounds() is not { } rect)
        {
            return false;
        }
        return ApplyCrop(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void PushAndRefresh(IUndoCommand command)
    {
        History.Push(command);
        RebuildRows();
        Selected = Rows.FirstOrDefault(r => r.Id == Selected?.Id) ?? Selected;
        OnHistoryChanged();
        RaiseDocumentChanged();
    }

    public double ActiveOpacity
    {
        get => ActiveLayer?.Opacity ?? 1.0;
        set
        {
            if (ActiveLayer is not { } layer || !double.IsFinite(value))
            {
                return;
            }
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(layer.Opacity - clamped) < 0.0001)
            {
                return;
            }
            PushAndRefresh(new SetLayerAppearanceCommand(layer, null, clamped));
            OnPropertyChanged(nameof(ActiveOpacity));
        }
    }

    public BlendMode ActiveBlend
    {
        get => ActiveLayer?.Blend ?? BlendMode.Normal;
        set
        {
            if (ActiveLayer is not { } layer || layer.Blend == value)
            {
                return;
            }
            PushAndRefresh(new SetLayerAppearanceCommand(layer, value, null));
            OnPropertyChanged(nameof(ActiveBlend));
        }
    }

    /// <summary>Scale of the active layer as a percentage of its pixel size (100 = 1:1).</summary>
    public double ActiveScalePercent
    {
        get
        {
            if (ActiveLayer is not { } layer)
            {
                return 100.0;
            }
            var pw = layer.Pixels?.Width ?? Doc.Width;
            return Math.Round(layer.Transform.Width / Math.Max(1, pw) * 100.0, 1, MidpointRounding.AwayFromZero);
        }
        set
        {
            if (ActiveLayer is not { } layer || !double.IsFinite(value) || value <= 0)
            {
                return;
            }
            var pw = layer.Pixels?.Width ?? Doc.Width;
            var ph = layer.Pixels?.Height ?? Doc.Height;
            var next = layer.Transform.Scaled(value, pw, ph).Rounded();
            if (next == layer.Transform)
            {
                return;
            }
            PushAndRefresh(new SetLayerTransformCommand(layer, next));
            OnPropertyChanged(nameof(ActiveScalePercent));
        }
    }

    public double ActiveRotation
    {
        get => ActiveLayer?.Transform.RotationDegrees ?? 0.0;
        set
        {
            if (ActiveLayer is not { } layer || !double.IsFinite(value))
            {
                return;
            }
            var next = layer.Transform with { RotationDegrees = value };
            if (next == layer.Transform)
            {
                return;
            }
            PushAndRefresh(new SetLayerTransformCommand(layer, next));
            OnPropertyChanged(nameof(ActiveRotation));
        }
    }

    /// <summary>Wraps the selected layer(s) into a new folder (upstream groupSelectedLayers).</summary>
    public void GroupSelected()
    {
        if (_selected is null)
        {
            return;
        }
        var members = Doc.Layers.Where(l => l.Id == _selected.Id).ToList();
        if (members.Count == 0)
        {
            return;
        }
        var groupId = members[0].Id;
        PushAndRefresh(new GroupLayersCommand(Doc, members));
        Selected = Rows.FirstOrDefault(r => r.Id == groupId);
    }

    /// <summary>New empty folder at the active layer's stack position (upstream addGroup).</summary>
    public void AddGroup()
    {
        var group = Layer.Group();
        var index = ActiveLayer is { } active ? Doc.Layers.IndexOf(active) + 1 : Doc.Layers.Count;
        PushAndRefresh(new AddGroupCommand(Doc, group, index));
        Selected = Rows.FirstOrDefault(r => r.Id == group.Id);
    }

    /// <summary>Merges the active layer into the pixel layer beneath it (upstream merge down).</summary>
    public void MergeDown()
    {
        if (ActiveLayer is not { } active || active.IsGroup || active.Pixels is null)
        {
            return;
        }
        var index = Doc.Layers.IndexOf(active);
        var below = Doc.Layers.Take(index).LastOrDefault(l => l.ParentId == active.ParentId);
        if (below is null || below.IsGroup || below.Pixels is null)
        {
            return;
        }
        PushAndRefresh(new MergeLayersCommand(Doc, [below, active]));
        Selected = Rows.FirstOrDefault(r => r.Id == below.Id);
    }

    public void FlipActive(bool horizontally)
    {
        if (ActiveLayer is not { } layer || layer.Pixels is null)
        {
            return;
        }
        PushAndRefresh(new FlipLayerCommand(layer, horizontally));
    }

    public void FlipCanvas(bool horizontally) =>
        PushAndRefresh(new FlipCanvasCommand(Doc, horizontally));

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

    // ------------------------------------------------------------- adjustments

    /// <summary>Destructive per-layer adjustments, mirroring the upstream sheets.</summary>
    public enum AdjustmentKind { Levels, Curves, HueSaturation, Exposure, GradientMap }

    private AdjustmentKind? _openAdjustment;
    /// <summary>The sheet currently open, or null.</summary>
    public AdjustmentKind? OpenAdjustment
    {
        get => _openAdjustment;
        private set
        {
            if (_openAdjustment == value)
            {
                return;
            }

            _openAdjustment = value;
            OnPropertyChanged(nameof(OpenAdjustment));
        }
    }

    /// <summary>Live settings for the open sheet (reset on every open).</summary>
    public LevelsSettings LevelsState { get; private set; } = new();
    public CurvesSettings CurvesState { get; private set; } = new();
    public HueSaturationSettings HueSatState { get; private set; } = new();
    public ExposureSettings ExposureState { get; private set; } = new();
    public GradientMapSettings GradientMapState { get; private set; } = new();

    private RasterSurface? _adjustmentPreview;

    /// <summary>Bumps on every preview recompute; the canvas caches bitmaps by this.</summary>
    public long AdjustmentPreviewGeneration { get; private set; }

    /// <summary>
    /// Adjusted copy of the active layer for live preview, or null when settings
    /// are identity or no sheet is open. The canvas draws this over the raw layer.
    /// </summary>
    public RasterSurface? AdjustmentPreviewSurface => _adjustmentPreview;

    public SelectionClip? CurrentAdjustmentClip =>
        Doc.Selection is { IsEmpty: false } sel ? sel.Clip(Doc.Width, Doc.Height) : null;

    /// <summary>Replaces the Levels state with an auto strategy computed from the active layer.</summary>
    public void ApplyLevelsAuto(LevelsAuto mode)
    {
        if (ActiveLayer?.Pixels is not { } surface)
        {
            return;
        }

        LevelsState = mode.Settings(LevelsHistogram.Compute(surface, CurrentAdjustmentClip));
        OnPropertyChanged(nameof(LevelsState));
        UpdateAdjustmentPreview();
    }

    private static bool IsCurvesIdentity(CurvesSettings c)
    {
        foreach (var points in c.Channels)
        {
            if (points.Length != 2 || points[0] != new CurvePoint(0, 0) || points[1] != new CurvePoint(255, 255))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsExposureIdentity(ExposureSettings e) =>
        e.Exposure == 0 && e.Offset == 0 && e.Gamma == 1;

    private RasterSurface? RunAdjustment(AdjustmentKind kind, RasterSurface surface, SelectionClip? clip) => kind switch
    {
        AdjustmentKind.Levels => LevelsState.IsIdentity
            ? null
            : AdjustmentRunner.ApplyTables(surface, LevelsState.BuildTables(), clip),
        AdjustmentKind.Curves => !CurvesState.IsValid || IsCurvesIdentity(CurvesState)
            ? null
            : AdjustmentRunner.ApplyTables(surface, CurvesState.BuildTables(), clip),
        AdjustmentKind.HueSaturation => HueSatState.IsIdentity
            ? null
            : AdjustmentRunner.ApplyHueSaturation(surface, HueSatState, clip),
        AdjustmentKind.Exposure => !ExposureState.IsValid || IsExposureIdentity(ExposureState)
            ? null
            : AdjustmentRunner.ApplyExposure(surface, ExposureState, clip),
        AdjustmentKind.GradientMap => GradientMapState.IsValid
            ? AdjustmentRunner.ApplyGradientMap(surface, GradientMapState, clip)
            : null,
        _ => null,
    };

    /// <summary>
    /// Opens an adjustment sheet on the active layer. Fails when another sheet
    /// is open, a floating selection is pending, or the layer is missing/locked.
    /// </summary>
    public bool OpenAdjustmentSheet(AdjustmentKind kind)
    {
        if (OpenAdjustment is not null || Floating is not null)
        {
            return false;
        }

        var layer = ActiveLayer;
        if (layer is null || layer.IsLocked)
        {
            return false;
        }

        layer.Pixels ??= new RasterSurface(Doc.Width, Doc.Height);
        LevelsState = new LevelsSettings();
        CurvesState = new CurvesSettings();
        HueSatState = new HueSaturationSettings();
        ExposureState = new ExposureSettings();
        GradientMapState = new GradientMapSettings();
        OnPropertyChanged(nameof(LevelsState));
        OnPropertyChanged(nameof(CurvesState));
        OnPropertyChanged(nameof(HueSatState));
        OnPropertyChanged(nameof(ExposureState));
        OnPropertyChanged(nameof(GradientMapState));
        OpenAdjustment = kind;
        UpdateAdjustmentPreview();
        return true;
    }

    /// <summary>Recomputes the preview from the current sheet settings.</summary>
    public void UpdateAdjustmentPreview()
    {
        if (OpenAdjustment is not { } kind || ActiveLayer?.Pixels is not { } surface)
        {
            _adjustmentPreview = null;
            return;
        }

        _adjustmentPreview = RunAdjustment(kind, surface, CurrentAdjustmentClip);
        AdjustmentPreviewGeneration++;
        DocumentChanged?.Invoke();
    }

    /// <summary>Applies the open sheet to the layer as one undoable command.</summary>
    public bool CommitAdjustment()
    {
        if (OpenAdjustment is not { } kind || ActiveLayer?.Pixels is not { } surface)
        {
            return false;
        }

        var adjusted = _adjustmentPreview ?? RunAdjustment(kind, surface, CurrentAdjustmentClip);
        if (adjusted is null)
        {
            return CancelAdjustment(); // identity: nothing to commit
        }

        var before = (byte[])surface.Pixels.Clone();
        var after = (byte[])adjusted.Pixels.Clone();
        History.Push(new AdjustmentCommand(surface, before, after, kind.ToString()));
        _adjustmentPreview = null;
        OpenAdjustment = null;
        RaiseDocumentChanged();
        return true;
    }

    /// <summary>Closes the sheet and drops the preview without touching pixels.</summary>
    public bool CancelAdjustment()
    {
        if (OpenAdjustment is null)
        {
            return false;
        }

        _adjustmentPreview = null;
        OpenAdjustment = null;
        RaiseDocumentChanged();
        return true;
    }

    /// <summary>Inverts the active layer's colors (Cmd+I upstream), one undoable command.</summary>
    public bool ApplyInvert()
    {
        if (OpenAdjustment is not null || Floating is not null)
        {
            return false;
        }

        var layer = ActiveLayer;
        if (layer is null || layer.IsLocked)
        {
            return false;
        }

        layer.Pixels ??= new RasterSurface(Doc.Width, Doc.Height);
        var surface = layer.Pixels;
        var adjusted = AdjustmentRunner.ApplyInvert(surface, CurrentAdjustmentClip);
        var before = (byte[])surface.Pixels.Clone();
        var after = (byte[])adjusted.Pixels.Clone();
        History.Push(new AdjustmentCommand(surface, before, after, "Invert"));
        RaiseDocumentChanged();
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
