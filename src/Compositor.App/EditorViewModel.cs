using System.Collections.ObjectModel;
using System.ComponentModel;
using Compositor.App.Imaging;
using Compositor.Core;
using Compositor.Core.Adjustments;
using Compositor.Core.Filters;
using Compositor.Core.Commands;
using Compositor.Core.Imaging;
using Compositor.Core.Project;
using Compositor.Core.Selection;
using Compositor.Core.Tools;

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

    /// <summary>Undoable steps on the shared history (see <see cref="UndoHistory.Depth"/>).</summary>
    public int HistoryDepth => History.Depth;
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

    // ---- Extended paint tools (gradient / shape / blur / smudge / clone / wand) ----

    /// <summary>Magic wand flood-fill tolerance (0..255 channel distance).</summary>
    public int WandTolerance { get; set; } = 32;

    /// <summary>Clone stamp source point (upstream Option-click).</summary>
    public (float X, float Y)? CloneSource { get; private set; }

    /// <summary>Clone aligned mode keeps the first stroke's offset between strokes.</summary>
    public bool CloneAligned { get; set; } = true;

    private (int Dx, int Dy)? _cloneAlignedOffset;

    public void SetCloneSource(float x, float y)
    {
        CloneSource = (x, y);
        _cloneAlignedOffset = null; // upstream: a new source starts a new alignment
    }

    /// <summary>Shape tool: fill with the brush color inside the dragged rect.</summary>
    public ShapeKind ShapeKind { get; set; } = ShapeKind.Rectangle;

    /// <summary>Gradient tool options.</summary>
    public GradientSettings GradientOptions { get; set; } = new();

    private (float X, float Y)? _dragAnchor;      // gradient / shape start
    private (float X, float Y)? _dragLast;        // gradient / shape current end
    private StrokeCoverage? _toolCoverage;        // blur / clone
    private byte[]? _toolSample;                  // blur / clone sample
    private SmudgeStroke? _smudge;                // smudge
    private byte[]? _toolBefore;                  // full before-buffer for RegionCommand
    private RasterSurface? _toolSurface;
    private SelectionClip? _toolClip;

    /// <summary>
    /// A document point expressed in the layer's own pixel space. Rendering places a layer
    /// through its transform, so editing has to run the same mapping backwards; without it
    /// the ink lands where the layer used to be and not under the cursor.
    /// </summary>
    private (float X, float Y) LayerPaintPoint(Layer layer, RasterSurface surface, float docX, float docY)
    {
        if (surface.Width == Doc.Width && surface.Height == Doc.Height
            && LayerPlacement.IsIdentity(layer.Transform, Doc.Width, Doc.Height))
        {
            return (docX, docY); // the ordinary case, no mapping involved
        }

        var (x, y) = LayerPlacement.MapFor(
            layer.Transform, surface.Width, surface.Height, Doc.Width, Doc.Height).ToLayer(docX, docY);
        return ((float)x, (float)y);
    }

    /// <summary>
    /// The clip a tool has to respect, in the layer's own pixel space. A selection is drawn
    /// on the canvas, so a transformed layer needs it pushed back through the same mapping
    /// the brush uses; otherwise the selection bounds the place the layer used to be.
    /// </summary>
    private SelectionClip? ClipForLayer(Layer layer, RasterSurface surface)
    {
        if (Doc.Selection is not { IsEmpty: false } selection)
        {
            return null;
        }

        var clip = selection.Clip(Doc.Width, Doc.Height);
        if (surface.Width == Doc.Width && surface.Height == Doc.Height
            && LayerPlacement.IsIdentity(layer.Transform, Doc.Width, Doc.Height))
        {
            return clip;
        }

        return LayerPlacement.MapClipToLayer(
            layer.Transform, clip, Doc.Width, Doc.Height, surface.Width, surface.Height);
    }

    /// <summary>
    /// Pointer-down dispatch for the paint-family tools (brush, blur, smudge,
    /// clone, gradient, shape). Magic wand is click-once: it selects and
    /// returns. Returns false when the tool did not take the drag.
    /// </summary>
    public bool BeginTool(float docX, float docY)
    {
        if (Tool == EditorTool.MagicWand)
        {
            if (ActiveLayer?.Pixels is { } wandSurface)
            {
                Doc.Selection = MagicWand.Select(
                    wandSurface, (int)docX, (int)docY, WandTolerance, SelectionMode.Replace);
                RaiseDocumentChanged();
            }
            return false;
        }

        var layer = ActiveLayer;
        if (layer is null || layer.IsLocked)
        {
            return false;
        }

        var surface = layer.Pixels ??= new RasterSurface(Doc.Width, Doc.Height);

        // From here on the tool's coordinates are layer coordinates: every branch below
        // writes into the layer's own buffer, which is what the placement transform maps.
        var paint = LayerPaintPoint(layer, surface, docX, docY);
        docX = paint.X;
        docY = paint.Y;

        _toolClip = ClipForLayer(layer, surface);

        switch (Tool)
        {
            case EditorTool.Brush:
                var started = BeginStroke(docX, docY);
                if (started)
                {
                    _toolSurface = surface;
                    _toolBefore = (byte[])surface.Pixels.Clone(); // guard marker; real snapshot lives in _strokeBefore
                }
                return started;

            case EditorTool.Blur:
                _toolSurface = surface;
                _toolBefore = (byte[])surface.Pixels.Clone();
                _toolSample = BlurStroke.GaussianBlur(
                    _toolBefore, surface.Width, surface.Height,
                    BlurStroke.SigmaFor(_brushDiameter));
                _toolCoverage = new StrokeCoverage(surface.Width, surface.Height,
                    SnapshotBrushSettings() with { Erasing = false }, _toolClip);
                _toolCoverage.WalkTo(docX, docY);
                _toolCoverage.PaintSampleRegion(surface, _toolBefore, _toolSample);
                break;

            case EditorTool.CloneStamp:
                if (CloneSource is not { } source)
                {
                    return false; // upstream: no source, no stamp
                }
                _toolSurface = surface;
                _toolBefore = (byte[])surface.Pixels.Clone();
                _toolSample = (byte[])surface.Pixels.Clone();
                var offset = CloneStroke.OffsetFor(source, (docX, docY), _cloneAlignedOffset);
                if (_cloneAlignedOffset is null)
                {
                    _cloneAlignedOffset = offset; // first stroke fixes the alignment
                }
                _toolCoverage = new StrokeCoverage(surface.Width, surface.Height,
                    SnapshotBrushSettings() with { Erasing = false }, _toolClip);
                _toolCoverage.WalkTo(docX, docY);
                CloneStampPaint(offset);
                break;

            case EditorTool.Smudge:
                _toolSurface = surface;
                _toolBefore = (byte[])surface.Pixels.Clone();
                _smudge = new SmudgeStroke(surface.Width, surface.Height,
                    SnapshotBrushSettings() with { Erasing = false }, surface.Pixels);
                _smudge.WalkTo(docX, docY, surface.Pixels);
                break;

            case EditorTool.Gradient:
            case EditorTool.Shape:
                _toolSurface = surface;
                _toolBefore = (byte[])surface.Pixels.Clone();
                _dragAnchor = (docX, docY);
                _dragLast = (docX, docY);
                break;

            default:
                return false;
        }

        IsStrokeActive = true;
        return true;
    }

    /// <summary>Pointer-move dispatch for the active paint tool.</summary>
    public void ContinueTool(float docX, float docY)
    {
        if (!IsStrokeActive || _toolSurface is null)
        {
            return;
        }

        if (ActiveLayer is { } strokeLayer)
        {
            var paint = LayerPaintPoint(strokeLayer, _toolSurface, docX, docY);
            docX = paint.X;
            docY = paint.Y;
        }

        if (Tool == EditorTool.Brush)
        {
            ContinueStroke(docX, docY);
            return;
        }

        if (_smudge is { } smudge)
        {
            smudge.WalkTo(docX, docY, _toolSurface.Pixels);
            return;
        }

        if (_toolCoverage is { } coverage && _toolSample is { } sample && _toolBefore is { } before)
        {
            coverage.WalkTo(docX, docY);
            if (Tool == EditorTool.CloneStamp && CloneSource is { } cloneSource)
            {
                CloneStampPaint(CloneStroke.OffsetFor(
                    cloneSource, (docX, docY), _cloneAlignedOffset));
            }
            else
            {
                coverage.PaintSampleRegion(_toolSurface, before, sample);
            }
            return;
        }

        if (_dragAnchor is not null)
        {
            _dragLast = (docX, docY);
        }
    }

    /// <summary>Pointer-up dispatch: finalizes and files the undo command.</summary>
    public bool EndTool()
    {
        if (!IsStrokeActive || _toolSurface is null || _toolBefore is null)
        {
            return false;
        }

        if (Tool == EditorTool.Brush)
        {
            var ended = EndStroke();
            _toolSurface = null;
            _toolBefore = null;
            IsStrokeActive = false;
            return ended;
        }

        var (w, h) = (_toolSurface.Width, _toolSurface.Height);
        switch (Tool)
        {
            case EditorTool.Blur:
            case EditorTool.CloneStamp:
                if (_toolCoverage is { } coverage)
                {
                    if (Tool == EditorTool.CloneStamp && CloneSource is { } cloneSource && _dragLast is { } lastPoint)
                    {
                        CloneStampPaint(CloneStroke.OffsetFor(cloneSource, lastPoint, _cloneAlignedOffset));
                    }
                    else if (_toolSample is { } sample)
                    {
                        coverage.PaintSampleRegion(_toolSurface, _toolBefore, sample);
                    }
                }
                break;

            case EditorTool.Smudge:
                _smudge?.Commit(_toolSurface.Pixels);
                break;

            case EditorTool.Gradient:
                if (_dragAnchor is { } g0 && _dragLast is { } g1)
                {
                    GradientFill.Apply(_toolSurface, g0.X, g0.Y, g1.X, g1.Y,
                        _brushR, _brushG, _brushB,
                        (byte)(255 - _brushR), (byte)(255 - _brushG), (byte)(255 - _brushB),
                        GradientOptions, _toolClip);
                }
                break;

            case EditorTool.Shape:
                if (_dragAnchor is { } s0 && _dragLast is { } s1)
                {
                    ShapeRasterizer.Fill(_toolSurface,
                        MathF.Min(s0.X, s1.X), MathF.Min(s0.Y, s1.Y),
                        MathF.Abs(s1.X - s0.X), MathF.Abs(s1.Y - s0.Y),
                        ShapeKind, 0f,
                        _brushR, _brushG, _brushB, 1f, _toolClip);
                }
                break;
        }

        var bounds = _dragAnchor is { } a && _dragLast is { } l
            ? BrushStroke.Bounds([a, l], _brushDiameter, w, h)
            : (0, 0, w, h);
        History.Push(new RegionCommand(_toolSurface, _toolBefore, bounds));
        OnHistoryChanged();

        _toolSurface = null;
        _toolBefore = null;
        _toolSample = null;
        _toolCoverage = null;
        _smudge = null;
        _dragAnchor = null;
        _dragLast = null;
        _toolClip = null;
        IsStrokeActive = false;
        RaiseDocumentChanged();
        return true;
    }

    private void CloneStampPaint((int Dx, int Dy) offset)
    {
        if (_toolSurface is null || _toolBefore is null || _toolSample is null || _toolCoverage is null)
        {
            return;
        }
        // Rebuild the shifted sample for the current offset, then paint through.
        var shifted = new byte[_toolSample.Length];
        for (var y = 0; y < _toolSurface.Height; y++)
        {
            var sy = y + offset.Dy;
            if (sy < 0 || sy >= _toolSurface.Height)
            {
                continue;
            }
            for (var x = 0; x < _toolSurface.Width; x++)
            {
                var sx = x + offset.Dx;
                if (sx < 0 || sx >= _toolSurface.Width)
                {
                    continue;
                }
                Array.Copy(_toolSample, ((sy * _toolSurface.Width) + sx) * 4,
                    shifted, ((y * _toolSurface.Width) + x) * 4, 4);
            }
        }
        _toolCoverage.PaintSampleRegion(_toolSurface, _toolBefore, shifted);
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
    private SelectionClip? _strokeClip;
    private StrokeCoverage? _strokeCoverage;
    private BrushSettings? _strokeSettings;

    /// <summary>Which tool the canvas pointer feeds (brush or a selection kind).</summary>
    public enum EditorTool
    {
        Brush, RectangleSelect, EllipseSelect, LassoSelect,
        MagicWand, Gradient, Shape, Blur, Smudge, CloneStamp,
        Move,
    }

    public static bool IsSelectTool(EditorTool tool) =>
        tool is EditorTool.RectangleSelect or EditorTool.EllipseSelect or EditorTool.LassoSelect;

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
    /// Starts a stroke on the active layer in the layer's own pixel coordinates
    /// (BeginTool hands over already-mapped points). Materializes the layer's pixel
    /// surface if it is still blank.
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
        _strokeClip = ClipForLayer(layer, _strokeSurface);
        _strokeSettings = SnapshotBrushSettings();
        _strokeCoverage = new StrokeCoverage(_strokeSurface.Width, _strokeSurface.Height, _strokeSettings, _strokeClip);
        _strokeCoverage.WalkTo(docX, docY); // first dab lands immediately (upstream walk)
        _strokeCoverage.PaintRegion(_strokeSurface, _strokeBefore);
        IsStrokeActive = true;
        return true;
    }

    /// <summary>
    /// Extends the in-progress stroke to a new point in the layer's pixel space, laying
    /// dabs at even spacing for immediate feedback. Undo is recorded once per whole
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

    // MARK: Transform drag
    //
    // Dragging a handle on the box writes LayerTransform directly, and only the finished drag becomes one undo
    // step. Upstream does the same: the drag edits a draft, and committing it is a single SetLayerTransform.

    private LayerTransform? _dragOriginal;
    private LayerTransform _dragBox;
    private double _dragStartX;
    private double _dragStartY;
    private TransformDragMode _dragMode;
    private int _dragIndex;

    /// <summary>True while a handle is being dragged.</summary>
    public bool IsTransformDragActive => _dragOriginal is not null;

    /// <summary>
    /// Document pixels per screen pixel at the current zoom, or 1 when the canvas is not laid out yet.
    /// The grab radius is authored in screen pixels, so it converts through this.
    /// </summary>
    public double DocPerScreenPixel(double viewportW, double viewportH)
    {
        var r = CanvasRect(viewportW, viewportH);
        return r.W > 0 ? Doc.Width / r.W : 1.0;
    }

    /// <summary>
    /// Screen to document coordinates without the canvas bound: the transform box and its handles sit outside
    /// the canvas too, and those have to stay grabbable.
    /// </summary>
    public (double X, double Y) ScreenToDocUnclamped(double sx, double sy, double viewportW, double viewportH)
    {
        var r = CanvasRect(viewportW, viewportH);
        if (r.W <= 0 || r.H <= 0)
        {
            return (0, 0);
        }

        return ((sx - r.X) * Doc.Width / r.W, (sy - r.Y) * Doc.Height / r.H);
    }

    /// <summary>
    /// The box a drag actually works on. A fresh blank layer carries a zero-size transform meaning "cover the
    /// whole canvas", which rendering resolves at draw time; taken literally its eight handles would all sit on
    /// one point, so it is resolved here too. A drag then writes back a real box, which is what a moved layer
    /// should carry.
    /// </summary>
    private LayerTransform DragBox(LayerTransform transform)
        => transform.CoversCanvas ? LayerTransform.ForCanvas(Doc.Width, Doc.Height) : transform;

    /// <summary>
    /// Grabs a handle under the pointer and starts a drag. False when the move tool is not active, no layer is
    /// selected, or the pointer is not on a handle.
    /// </summary>
    public bool BeginTransformDrag(double docX, double docY, double viewportW, double viewportH)
    {
        if (Tool != EditorTool.Move || ActiveLayer is not { } layer)
        {
            return false;
        }

        var box = DragBox(layer.Transform);
        var perPixel = DocPerScreenPixel(viewportW, viewportH);
        var hit = TransformHandles.Hit(
            box, docX, docY,
            TransformHandles.ScreenGrabRadius * perPixel,
            TransformHandles.RotationHandleScreenOffset * perPixel);
        if (hit.IsNone)
        {
            // Not on a handle: pressing inside the box drags the whole layer, as the move tool does upstream.
            if (!box.Contains(docX, docY))
            {
                return false;
            }

            hit = new TransformHit(TransformDragMode.Move, 0);
        }

        _dragOriginal = layer.Transform;
        _dragBox = box;
        _dragStartX = docX;
        _dragStartY = docY;
        _dragMode = hit.Mode;
        _dragIndex = hit.Index;
        return true;
    }

    /// <summary>
    /// The drag so far, applied to the layer as it goes so the canvas follows the pointer. No undo step is
    /// pushed until the drag ends.
    /// </summary>
    public void ContinueTransformDrag(double docX, double docY, bool shift = false, bool fromCenter = false)
    {
        if (_dragOriginal is null || ActiveLayer is not { } layer)
        {
            return;
        }

        var box = _dragBox;
        var next = _dragMode switch
        {
            TransformDragMode.Move => TransformDrag.Move(box, _dragStartX, _dragStartY, docX, docY, shift),
            TransformDragMode.Rotate => TransformDrag.Rotate(box, _dragStartX, _dragStartY, docX, docY, shift),
            _ => TransformDrag.Resize(box, _dragIndex, _dragStartX, _dragStartY, docX, docY,
                                      lockRatio: false, shift: shift, fromCenter: fromCenter),
        };

        if (next == layer.Transform)
        {
            return;
        }

        layer.Transform = next;
        RaiseDocumentChanged();
        OnPropertyChanged(nameof(ActiveScalePercent));
        OnPropertyChanged(nameof(ActiveRotation));
    }

    /// <summary>
    /// Ends the drag and records it as one undo step, on whole pixels and whole degrees. False when nothing
    /// moved, so a click on a handle does not leave a no-op in the history.
    /// </summary>
    public bool EndTransformDrag()
    {
        if (_dragOriginal is not { } original || ActiveLayer is not { } layer)
        {
            _dragOriginal = null;
            return false;
        }

        _dragOriginal = null;

        // Compared against the layer's own value, not the resolved box: a click that never moved the box must
        // not leave an undo step, and a cover-canvas transform must not be turned into a 1x1 one by rounding.
        if (layer.Transform == original)
        {
            RaiseDocumentChanged();
            return false;
        }

        var settled = layer.Transform.Rounded();

        // Put the layer back before constructing the command: it captures the transform it finds as the state
        // undo returns to, so leaving the dragged value in place would make undo a no-op.
        layer.Transform = original;

        PushAndRefresh(new SetLayerTransformCommand(layer, settled));
        OnPropertyChanged(nameof(ActiveScalePercent));
        OnPropertyChanged(nameof(ActiveRotation));
        return true;
    }

    /// <summary>
    /// Where to draw the box's handles, in document pixels, plus the rotation handle. Null when the move tool
    /// is not active or no layer is selected.
    /// </summary>
    public ((double X, double Y)[] Handles, (double X, double Y) Rotation)? TransformOverlay(double viewportW, double viewportH)
    {
        if (Tool != EditorTool.Move || ActiveLayer is not { } layer)
        {
            return null;
        }

        var perPixel = DocPerScreenPixel(viewportW, viewportH);
        var box = DragBox(layer.Transform);
        return (TransformHandles.Points(box),
                TransformHandles.RotationHandle(box, TransformHandles.RotationHandleScreenOffset * perPixel));
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
        ImageBudget.ValidateExport(Doc.Width, Doc.Height);
        var (_, _, rgba) = Flatten.ToRgba(Doc);
        using var output = File.Create(path);
        Png.Encode(output, Doc.Width, Doc.Height, rgba, dpi: Doc.Resolution);
    }

    /// <summary>
    /// Encodes the flattened canvas as JPEG without touching disk. The export sheet previews
    /// through this, which is how upstream's sheet shows an encoded size before you commit.
    /// </summary>
    public byte[] EncodeJpeg(JpegOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var (_, _, rgba) = Flatten.ToRgba(Doc);
        return SkiaCodec.EncodeJpeg(rgba, Doc.Width, Doc.Height, options, dpi: Doc.Resolution);
    }

    /// <summary>Writes the matte-flattened JPEG to disk at the document's resolution.</summary>
    public void ExportJpeg(string path, JpegOptions options)
    {
        ImageBudget.ValidateExport(Doc.Width, Doc.Height);
        File.WriteAllBytes(path, EncodeJpeg(options));
    }

    /// <summary>
    /// Canvas pixels already held by layers. Upstream spends the document pixel budget from
    /// this number, recomputed per file so a batch cannot collectively overrun it.
    /// </summary>
    public long UsedPixels
    {
        get
        {
            long total = 0;
            foreach (var layer in Doc.Layers)
            {
                if (layer.Pixels is { } surface)
                {
                    total += ImageBudget.PixelCount(surface.Width, surface.Height);
                }
            }

            return total;
        }
    }

    private string? _importError;

    /// <summary>
    /// What the last import batch could not read: one "file: reason" line per failure joined
    /// by blank lines, the shape upstream's <c>session.importError</c> uses. Null when clean.
    /// </summary>
    public string? ImportError
    {
        get => _importError;
        private set
        {
            if (_importError != value)
            {
                _importError = value;
                OnPropertyChanged(nameof(ImportError));
            }
        }
    }

    public static EditorViewModel LoadProject(string path)
    {
        var doc = ProjectStore.Load(path);
        return new EditorViewModel(doc) { _currentFilePath = path };
    }

    /// <summary>
    /// Imports one image file as a new topmost layer, named after the file. Returns null and
    /// sets <see cref="ImportError"/> when the file cannot be read.
    /// </summary>
    public Layer? ImportImage(string path, (float X, float Y)? at = null)
    {
        var added = ImportImages([path], at);
        return added.Count > 0 ? added[0] : null;
    }

    /// <summary>
    /// Imports decoded image bytes as a new topmost layer. Used by a drop that carries image
    /// data but no file (a screenshot, or a picture dragged out of a browser): upstream copies
    /// those bytes to a temporary file first, we hand them straight to the same decoder.
    /// </summary>
    public Layer? ImportImageBytes(byte[] bytes, string name, (float X, float Y)? at = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            var decoded = SkiaCodec.Decode(bytes, UsedPixels);
            var layer = ImportRgba(name, decoded.Width, decoded.Height, decoded.Rgba, at);
            ImportError = null;
            return layer;
        }
        catch (Exception ex) when (ex is ImageException or IOException or UnauthorizedAccessException)
        {
            ImportError = $"{name}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// A drop that held neither a file nor image data. Same message shape upstream's
    /// <c>ImageFileDrop</c> shows, with the format list read from the matrix so it cannot drift.
    /// </summary>
    public void ReportUnreadableDrop() =>
        ImportError = "Some dropped items couldn't be read. " +
                      ImageFormatPolicy.UnsupportedImportMessage.Replace("Choose a ", "Drag a ", StringComparison.Ordinal) +
                      " from Explorer.";

    /// <summary>
    /// Imports files in pasteboard order. Upstream drains a batch collecting a per-file error
    /// instead of aborting on the first unreadable drop; same here. Not an undo step yet:
    /// layer add/remove has no command type, which is the DocumentHistory coalescing gap.
    /// </summary>
    public IReadOnlyList<Layer> ImportImages(string[] paths, (float X, float Y)? at = null)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var added = new List<Layer>();
        var failures = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var decoded = SkiaCodec.Decode(bytes, UsedPixels);
                added.Add(ImportRgba(
                    Path.GetFileNameWithoutExtension(path),
                    decoded.Width,
                    decoded.Height,
                    decoded.Rgba,
                    at));
            }
            catch (Exception ex) when (ex is ImageException or IOException or UnauthorizedAccessException)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        ImportError = failures.Count > 0 ? string.Join("\n\n", failures) : null;
        if (added.Count > 0)
        {
            RaiseDocumentChanged();
        }

        return added;
    }

    /// <summary>
    /// Imports raw straight-alpha RGBA pixels as a new topmost layer. Placement follows
    /// upstream: centered on <paramref name="at"/> when given, otherwise on the canvas centre,
    /// then clipped to the canvas. Our layer surfaces are canvas-sized, so the placement is
    /// baked into the pixels instead of a transform.
    /// </summary>
    public Layer ImportRgba(string name, int width, int height, byte[] rgba, (float X, float Y)? at = null)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
        {
            throw new ArgumentException("Invalid image dimensions or buffer length.");
        }

        var surface = new RasterSurface(Doc.Width, Doc.Height);
        var originX = (int)Math.Floor((at?.X ?? (Doc.Width / 2f)) - (width / 2f));
        var originY = (int)Math.Floor((at?.Y ?? (Doc.Height / 2f)) - (height / 2f));
        var srcX = Math.Max(0, -originX);
        var srcY = Math.Max(0, -originY);
        var cols = Math.Min(width - srcX, Doc.Width - (originX + srcX));
        var rows = Math.Min(height - srcY, Doc.Height - (originY + srcY));
        if (cols > 0 && rows > 0)
        {
            var dstX = originX + srcX;
            var dstY = originY + srcY;
            for (var y = 0; y < rows; y++)
            {
                Array.Copy(
                    rgba,
                    ((y + srcY) * width * 4) + (srcX * 4),
                    surface.Pixels,
                    (((y + dstY) * Doc.Width) + dstX) * 4,
                    cols * 4);
            }
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

    /// <summary>Destructive per-layer adjustments, mirroring the upstream sheets. Grain sits here
    /// too because Filters.swift marks it an image adjustment, not a Filter-menu entry.</summary>
    public enum AdjustmentKind { Levels, Curves, HueSaturation, Exposure, GradientMap, Grain }

    /// <summary>The Filter menu: the Filters.swift entries that are not image adjustments.
    /// Remove Background and Content-Aware Fill stay out until the ML workstream.</summary>
    public enum FilterMenuKind { GaussianBlur, MotionBlur, AddNoise, LensCorrection }

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
    public GrainSettings GrainState { get; private set; } = new();

    /// <summary>Live settings for the open filter sheet (reset on every open).</summary>
    public FilterSettings FilterState { get; private set; } = new();

    /// <summary>
    /// Seed for the open sheet's noise or grain. One seed per sheet keeps a live preview from
    /// swimming while a slider moves; upstream gives each application its own seed, so a fresh
    /// number is drawn on open and reused for the whole preview/commit pair.
    /// </summary>
    public uint FilterSeed { get; private set; } = FilterRunner.DefaultSeed;

    private uint _nextFilterSeed = FilterRunner.DefaultSeed;

    /// <summary>Deterministic per application: tests need reproducible noise, so no RNG.</summary>
    private uint DrawFilterSeed()
    {
        unchecked
        {
            _nextFilterSeed = (_nextFilterSeed * 1664525u) + 1013904223u;
            return _nextFilterSeed;
        }
    }

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
        AdjustmentKind.Grain => GrainState.Amount <= 0
            ? null
            : FilterRunner.ApplyGrain(surface, GrainState, clip, seed: FilterSeed),
        _ => null,
    };

    /// <summary>
    /// Opens an adjustment sheet on the active layer. Fails when another sheet
    /// is open, a floating selection is pending, or the layer is missing/locked.
    /// </summary>
    public bool OpenAdjustmentSheet(AdjustmentKind kind)
    {
        if (OpenAdjustment is not null || OpenFilter is not null || Floating is not null)
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
        GrainState = new GrainSettings();
        OnPropertyChanged(nameof(LevelsState));
        OnPropertyChanged(nameof(CurvesState));
        OnPropertyChanged(nameof(HueSatState));
        OnPropertyChanged(nameof(ExposureState));
        OnPropertyChanged(nameof(GradientMapState));
        OnPropertyChanged(nameof(GrainState));
        FilterSeed = DrawFilterSeed();
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

    // ------------------------------------------------------------------ filters

    private FilterMenuKind? _openFilter;

    /// <summary>The Filter-menu sheet currently open, or null.</summary>
    public FilterMenuKind? OpenFilter
    {
        get => _openFilter;
        private set
        {
            if (_openFilter == value)
            {
                return;
            }

            _openFilter = value;
            OnPropertyChanged(nameof(OpenFilter));
        }
    }

    /// <summary>Opens a filter sheet on the active layer; false when another sheet is open.</summary>
    public bool OpenFilterSheet(FilterMenuKind kind)
    {
        if (OpenAdjustment is not null || OpenFilter is not null || Floating is not null)
        {
            return false;
        }

        var layer = ActiveLayer;
        if (layer is null || layer.IsLocked)
        {
            return false;
        }

        layer.Pixels ??= new RasterSurface(Doc.Width, Doc.Height);
        FilterState = new FilterSettings();
        FilterSeed = DrawFilterSeed();
        OnPropertyChanged(nameof(FilterState));
        OpenFilter = kind;
        UpdateFilterPreview();
        return true;
    }

    /// <summary>Replaces the filter settings and refreshes the preview. The records are immutable,
    /// so the sheet writes through here instead of holding a mutable settings object.</summary>
    public void SetFilterState(FilterSettings settings)
    {
        FilterState = settings;
        OnPropertyChanged(nameof(FilterState));
        UpdateFilterPreview();
    }

    /// <summary>Replaces the grain settings and refreshes the adjustment preview.</summary>
    public void SetGrainState(GrainSettings settings)
    {
        GrainState = settings;
        OnPropertyChanged(nameof(GrainState));
        UpdateAdjustmentPreview();
    }

    /// <summary>Recomputes the filter preview from the current sheet settings. Shares the preview
    /// slot and generation counter with the adjustment sheets, so the canvas needs no new wiring.</summary>
    public void UpdateFilterPreview()
    {
        if (OpenFilter is not { } kind || ActiveLayer?.Pixels is not { } surface)
        {
            _adjustmentPreview = null;
            return;
        }

        _adjustmentPreview = RunFilter(kind, surface, CurrentAdjustmentClip);
        AdjustmentPreviewGeneration++;
        DocumentChanged?.Invoke();
    }

    /// <summary>
    /// Runs the open filter, or returns null when its settings are an identity. Straightening by
    /// zero is the one identity case: the other filters are clamped away from doing nothing. The
    /// null lets commit behave like the adjustment sheets do, closing instead of writing a blank
    /// undo step the user would have to walk back through.
    /// </summary>
    private RasterSurface? RunFilter(FilterMenuKind kind, RasterSurface surface, SelectionClip? clip)
    {
        if (kind == FilterMenuKind.LensCorrection && FilterState.Normalize().Distortion == 0)
        {
            return null;
        }

        return FilterRunner.Apply(ToCoreKind(kind), surface, FilterState, clip, FilterSeed);
    }

    private static FilterKind ToCoreKind(FilterMenuKind kind) => kind switch
    {
        FilterMenuKind.GaussianBlur => FilterKind.GaussianBlur,
        FilterMenuKind.MotionBlur => FilterKind.MotionBlur,
        FilterMenuKind.AddNoise => FilterKind.AddNoise,
        FilterMenuKind.LensCorrection => FilterKind.LensCorrection,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Applies the open filter to the layer as one undoable command.</summary>
    public bool CommitFilter()
    {
        if (OpenFilter is not { } kind || ActiveLayer?.Pixels is not { } surface)
        {
            return false;
        }

        var filtered = _adjustmentPreview ?? RunFilter(kind, surface, CurrentAdjustmentClip);
        if (filtered is null)
        {
            return CancelFilter(); // identity: nothing to commit
        }

        var before = (byte[])surface.Pixels.Clone();
        var after = (byte[])filtered.Pixels.Clone();
        History.Push(new AdjustmentCommand(surface, before, after, kind.ToString()));
        _adjustmentPreview = null;
        OpenFilter = null;
        RaiseDocumentChanged();
        return true;
    }

    /// <summary>Closes the filter sheet and drops the preview without touching pixels.</summary>
    public bool CancelFilter()
    {
        if (OpenFilter is null)
        {
            return false;
        }

        _adjustmentPreview = null;
        OpenFilter = null;
        RaiseDocumentChanged();
        return true;
    }

    /// <summary>Inverts the active layer's colors (Cmd+I upstream), one undoable command.</summary>
    public bool ApplyInvert()
    {
        if (OpenAdjustment is not null || OpenFilter is not null || Floating is not null)
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

    /// <summary>
    /// Fills the current selection with matching pixels from around it (upstream's Content
    /// Fill). One undoable command, and no sheet: upstream has no settings for it either, the
    /// kernel just runs.
    /// </summary>
    /// <returns>
    /// False when there is nothing to fill, nothing to fill from, or a sheet is open. The
    /// layer is untouched in every one of those cases.
    /// </returns>
    public bool ContentAwareFill()
    {
        if (OpenAdjustment is not null || OpenFilter is not null || Floating is not null)
        {
            return false;
        }

        var layer = ActiveLayer;
        if (layer is null || layer.IsLocked)
        {
            return false;
        }

        if (Doc.Selection is not { IsEmpty: false } selection)
        {
            return false;
        }

        layer.Pixels ??= new RasterSurface(Doc.Width, Doc.Height);
        var surface = layer.Pixels;
        var after = (byte[])surface.Pixels.Clone();
        if (!Core.Imaging.ContentFill.Fill(after, surface.Width, surface.Height, selection.Clip(surface.Width, surface.Height)))
        {
            return false;
        }

        var before = (byte[])surface.Pixels.Clone();
        History.Push(new AdjustmentCommand(surface, before, after, "ContentAwareFill"));
        RaiseDocumentChanged();
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
