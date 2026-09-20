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

        var canvasRect = FitRect(bounds, ViewModel.Doc.Width, ViewModel.Doc.Height);
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
                var bitmap = GetBitmap(layer, surface);
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
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (ViewModel is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var doc = ToDocCoords(e.GetPosition(this));
        if (doc is null)
        {
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
        if (ViewModel?.IsStrokeActive != true)
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
    private (float X, float Y)? ToDocCoords(Point p)
    {
        if (ViewModel is null)
        {
            return null;
        }

        var rect = FitRect(new Rect(Bounds.Size), ViewModel.Doc.Width, ViewModel.Doc.Height);
        if (rect.Width <= 0 || rect.Height <= 0 ||
            p.X < rect.X || p.Y < rect.Y || p.X > rect.Right || p.Y > rect.Bottom)
        {
            return null;
        }

        return (
            (float)((p.X - rect.X) * ViewModel.Doc.Width / rect.Width),
            (float)((p.Y - rect.Y) * ViewModel.Doc.Height / rect.Height));
    }

    /// <summary>Layer pixel preview, rebuilt only when the surface Version moved.</summary>
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

    private void Detach()
    {
        DetachRows();
        if (_attached is null)
        {
            return;
        }

        _attached.DocumentChanged -= OnDocumentChanged;
        _attached.Rows.CollectionChanged -= OnRowsChanged;
        _attached = null;
        foreach (var entry in _pixelCache.Values)
        {
            entry.Bitmap.Dispose();
        }

        _pixelCache.Clear();
    }

    private static Rect FitRect(Rect bounds, int docWidth, int docHeight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || docWidth <= 0 || docHeight <= 0)
        {
            return default;
        }

        var scale = Math.Min(bounds.Width / docWidth, bounds.Height / docHeight);
        var w = docWidth * scale;
        var h = docHeight * scale;
        return new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h);
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
