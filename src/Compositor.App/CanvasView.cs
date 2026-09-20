using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Compositor.Core;

namespace Compositor.App;

/// <summary>
/// Editor canvas: draws the working-area checkerboard and one translucent
/// rect per visible layer (pixel compositing arrives with the raster engine).
/// Document space is fit into the control and centered.
/// </summary>
public sealed class CanvasView : Control
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

    static CanvasView()
    {
        AffectsRender<CanvasView>(ViewModelProperty);
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

            context.FillRectangle(
                LayerTints[tint % LayerTints.Length],
                Scaled(canvasRect, ViewModel.Doc, layer.Transform));
            tint++;
        }
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
