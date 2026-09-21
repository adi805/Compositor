using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using Compositor.Core;

namespace Compositor.App;

/// <summary>
/// Editor canvas: checkerboard backdrop, real layer pixels rendered from each
/// layer's RasterSurface via cached WriteableBitmaps (invalidated by surface
/// Version), and pointer-driven brush strokes in document coordinates.
/// Implements ICustomHitTest because a plain Control with no drawn content is
/// otherwise invisible to Avalonia's hit tester.
/// </summary>
public sealed class CanvasView : Control, ICustomHitTest
{
    private static readonly Brush CheckerDark = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private static readonly Brush CheckerLight = new SolidColorBrush(Color.FromRgb(58, 58, 58));
    private static readonly IBrush[] LayerTints =
    {
        new SolidColorBrush(Color.FromArgb(90, 66, 133, 244)),
        new SolidColorBrush(Color.FromArgb(90, 219, 68, 55)),
        new SolidColorBrush(Color.FromArgb(90, 244, 180, 0)),
        new SolidColorBrush(Color.FromArgb(90, 15, 157, 88)),
    };

    private const double CheckerSize = 12;
    private const double MinLayerSize = 1;

    public static readonly StyledProperty<EditorViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<CanvasView, EditorViewModel?>(nameof(ViewModel));

    public EditorViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private readonly Dictionary<Layer, (WriteableBitmap Bitmap, long Version)> _pixelCache = new();
    private readonly List<LayerRow> _attachedRows = new();
    private EditorViewModel? _attached;
    private Point? _panLast;

    public CanvasView()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// <summary>Whole control area accepts pointer input (ICustomHitTest).</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    static CanvasView()
    {
        AffectsRender<CanvasView>(ViewModelProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModelProperty)
        {
            Detach();
            _attached = ViewModel;
            if (_attached is not null)
            {
                _attached.DocumentChanged += OnDocumentChanged;
                _attached.Rows.CollectionChanged += OnRowsChanged;
                _attached.ViewChanged += OnViewChanged;
                AttachRows();
            }

            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(CheckerDark, bounds);

        if (ViewModel is null)
        {
            return;
        }

        var (crX, crY, crW, crH) = ViewModel.CanvasRect(bounds.Width, bounds.Height);
        var canvasRect = new Rect(crX, crY, crW, crH);
        if (canvasRect.Width <= 0 || canvasRect.Height <= 0)
        {
            return;
        }

        for (var y = 0.0; y < canvasRect.Height; y += CheckerSize)
        {
            var row = (int)(y / CheckerSize);
            for (var x = (row % 2) * CheckerSize;
                 x < canvasRect.Width;
                 x += CheckerSize * 2)
            {
                var cell = new Rect(
                    canvasRect.X + x,
                    canvasRect.Y + y,
                    Math.Min(CheckerSize, canvasRect.Width - x),
                    Math.Min(CheckerSize, canvasRect.Height - y));
                context.FillRectangle(CheckerLight, cell);
            }
        }

        var tint = 0;
        foreach (var layer in ViewModel.Doc.Layers)
        {
            if (!layer.IsVisible)
            {
                continue;
            }

            if (layer.Pixels is { } surface)
            {
                // Live adjustment preview replaces the active layer's own pixels.
                var preview = layer == ViewModel.ActiveLayer ? ViewModel.AdjustmentPreviewSurface : null;
                var bitmap = preview is not null ? GetPreviewBitmap(preview) : GetBitmap(layer, surface);
                if (bitmap is not null)
                {
                    context.DrawImage(bitmap, canvasRect);
                }
            }
            else
            {
                // Blank layer: placeholder rect so it stays visible in the canvas.
                context.FillRectangle(
                    LayerTints[tint % LayerTints.Length],
                    Scaled(canvasRect, ViewModel.Doc, layer.Transform));
            }

            tint++;
        }

        RenderSelectionOverlay(context, canvasRect);
    }

    private WriteableBitmap? _floatingBitmap;
    private Compositor.Core.Selection.FloatingSelection? _floatingSource;
    private static readonly IPen SelectionPen = new Pen(Brushes.Cyan, 1, DashStyle.Dash);

    /// <summary>Floating pixels, selection outline, and the live marquee draft.</summary>
    private void RenderSelectionOverlay(DrawingContext context, Rect canvasRect)
    {
        if (ViewModel is null)
        {
            return;
        }

        var doc = ViewModel.Doc;
        var scale = canvasRect.Width / doc.Width;

        if (ViewModel.Floating is { } floating)
        {
            if (_floatingBitmap is null || !ReferenceEquals(_floatingSource, floating))
            {
                _floatingBitmap?.Dispose();
                _floatingBitmap = CreateBitmap(floating.Width, floating.Height, floating.Pixels);
                _floatingSource = floating;
            }
            if (_floatingBitmap is not null)
            {
                var rect = new Rect(
                    canvasRect.X + (floating.X * scale),
                    canvasRect.Y + (floating.Y * scale),
                    floating.Width * scale,
                    floating.Height * scale);
                context.DrawImage(_floatingBitmap, rect);
                context.DrawRectangle(SelectionPen, rect);
            }
        }

        if (doc.Selection is { IsEmpty: false } selection)
        {
            var clip = selection.Clip(doc.Width, doc.Height);
            if (clip.Coverage is not null)
            {
                context.DrawRectangle(SelectionPen, new Rect(
                    canvasRect.X + (clip.X * scale),
                    canvasRect.Y + (clip.Y * scale),
                    clip.Width * scale,
                    clip.Height * scale));
            }
        }

        if (ViewModel.DraftBounds is { } draft)
        {
            context.DrawRectangle(SelectionPen, new Rect(
                canvasRect.X + (draft.X * scale),
                canvasRect.Y + (draft.Y * scale),
                draft.W * scale,
                draft.H * scale));
        }
    }

    private static WriteableBitmap? CreateBitmap(int width, int height, byte[] pixels)
    {
        try
        {
            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);
            using var fb = bitmap.Lock();
            Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
            return bitmap;
        }
        catch (Exception)
        {
            return null; // headless/no-render-context
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (ViewModel is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            // Start a view pan; stroke painting stays on the left button.
            _panLast = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var doc = ToDocCoords(e.GetPosition(this));
        if (doc is null)
        {
            return;
        }

        if (ViewModel.Tool != EditorViewModel.EditorTool.Brush)
        {
            if (ViewModel.BeginMarquee(doc.Value.X, doc.Value.Y))
            {
                e.Pointer.Capture(this);
                e.Handled = true;
                InvalidateVisual();
            }
            return;
        }

        if (ViewModel.BeginStroke(doc.Value.X, doc.Value.Y))
        {
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ViewModel is null)
        {
            return;
        }

        if (_panLast is { } last && e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
        {
            var pos = e.GetPosition(this);
            ViewModel.PanBy(pos.X - last.X, pos.Y - last.Y);
            _panLast = pos;
            e.Handled = true;
            return;
        }

        if (ViewModel.IsMarqueeActive)
        {
            var mdoc = ToDocCoords(e.GetPosition(this));
            if (mdoc is not null)
            {
                ViewModel.ContinueMarquee(mdoc.Value.X, mdoc.Value.Y);
                e.Handled = true;
                InvalidateVisual();
            }
            return;
        }

        if (!ViewModel.IsStrokeActive)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var doc = ToDocCoords(e.GetPosition(this));
        if (doc is null)
        {
            return;
        }

        ViewModel.ContinueStroke(doc.Value.X, doc.Value.Y);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panLast is not null)
        {
            _panLast = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (ViewModel?.IsMarqueeActive == true)
        {
            ViewModel.EndMarquee();
            e.Pointer.Capture(null);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        if (ViewModel?.IsStrokeActive != true)
        {
            return;
        }

        ViewModel.EndStroke();
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <summary>Control-space point to document coordinates; null when outside the canvas.</summary>
    private (float X, float Y)? ToDocCoords(Point p) =>
        ViewModel?.ScreenToDoc(p.X, p.Y, Bounds.Width, Bounds.Height);

    /// <summary>Layer pixel preview, rebuilt only when the surface Version moved.</summary>
    private WriteableBitmap? _previewBitmap;
    private long _previewGeneration = -1;

    /// <summary>Adjustment preview bitmap, rebuilt only when the preview generation moved.</summary>
    private WriteableBitmap? GetPreviewBitmap(RasterSurface preview)
    {
        var generation = ViewModel?.AdjustmentPreviewGeneration ?? -1;
        if (_previewBitmap is not null && _previewGeneration == generation)
        {
            return _previewBitmap;
        }

        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewGeneration = generation;
        WriteableBitmap bitmap;
        try
        {
            bitmap = new WriteableBitmap(
                new PixelSize(preview.Width, preview.Height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);
            using var fb = bitmap.Lock();
            Marshal.Copy(preview.Pixels, 0, fb.Address, preview.Pixels.Length);
        }
        catch (Exception)
        {
            return null;
        }

        _previewBitmap = bitmap;
        return bitmap;
    }

    private WriteableBitmap? GetBitmap(Layer layer, RasterSurface surface)
    {
        if (_pixelCache.TryGetValue(layer, out var cached))
        {
            if (cached.Version == surface.Version)
            {
                return cached.Bitmap;
            }

            cached.Bitmap.Dispose();
            _pixelCache.Remove(layer);
        }

        WriteableBitmap bitmap;
        try
        {
            bitmap = new WriteableBitmap(
                new PixelSize(surface.Width, surface.Height),
                new Vector(96, 96),
                PixelFormats.Rgba8888,
                AlphaFormat.Unpremul);
            using var fb = bitmap.Lock();
            // 32bpp: framebuffer stride equals width*4; copy straight through.
            Marshal.Copy(surface.Pixels, 0, fb.Address, surface.Pixels.Length);
        }
        catch (Exception)
        {
            return null; // headless/no-render-context: fall back to placeholder rendering
        }

        _pixelCache[layer] = (bitmap, surface.Version);
        return bitmap;
    }

    private void AttachRows()
    {
        DetachRows();
        if (_attached is null)
        {
            return;
        }

        foreach (var row in _attached.Rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
            _attachedRows.Add(row);
        }
    }

    private void DetachRows()
    {
        foreach (var row in _attachedRows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        _attachedRows.Clear();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerRow.IsVisible))
        {
            InvalidateVisual();
        }
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        AttachRows();
        InvalidateVisual();
    }

    private void OnDocumentChanged()
    {
        InvalidateVisual();
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (ViewModel is null)
        {
            return;
        }

        var pos = e.GetPosition(this);
        ViewModel.ZoomAt(pos.X, pos.Y, Bounds.Width, Bounds.Height, e.Delta.Y > 0 ? 1.25 : 0.8);
        e.Handled = true;
    }

    private void Detach()
    {
        DetachRows();
        _floatingBitmap?.Dispose();
        _floatingBitmap = null;
        _floatingSource = null;
        if (_attached is null)
        {
            return;
        }

        _attached.DocumentChanged -= OnDocumentChanged;
        _attached.Rows.CollectionChanged -= OnRowsChanged;
        _attached.ViewChanged -= OnViewChanged;
        _attached = null;
        foreach (var entry in _pixelCache.Values)
        {
            entry.Bitmap.Dispose();
        }

        _pixelCache.Clear();
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewGeneration = -1;
    }

    private static Rect Scaled(Rect canvasRect, Document doc, LayerTransform t)
    {
        var docW = (double)doc.Width;
        var docH = (double)doc.Height;
        var width = t.CoversCanvas ? docW : t.Width;
        var height = t.CoversCanvas ? docH : t.Height;
        return new Rect(
            canvasRect.X + (t.OriginX / docW * canvasRect.Width),
            canvasRect.Y + (t.OriginY / docH * canvasRect.Height),
            Math.Max(MinLayerSize, width / docW * canvasRect.Width),
            Math.Max(MinLayerSize, height / docH * canvasRect.Height));
    }
}
