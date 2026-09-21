namespace Compositor.Core.Selection;

/// <summary>
/// Clipboard contents for selection copy/cut: cropped RGBA plus its document
/// origin. Bytes are straight-alpha, same layout as RasterSurface.
/// </summary>
public sealed class SelectionClipboardData
{
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Pixels { get; init; } = [];
}

/// <summary>
/// A pasted selection floating above the document until committed or dropped
/// (upstream FloatingSelection). Pixels keep their own offset; Move nudges it;
/// Commit alpha-over composites into the active layer as one undo step.
/// </summary>
public sealed class FloatingSelection
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public FloatingSelection(byte[] pixels, int x, int y, int width, int height)
    {
        Pixels = pixels;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public void Move(int dx, int dy)
    {
        X += dx;
        Y += dy;
    }
}

/// <summary>
/// Selection copy/cut/paste plus the masked pixel blend used by adjustments,
/// following upstream PixelAdjust: coverage × patch + (1 − coverage) × original.
/// </summary>
public static class SelectionOps
{
    /// <summary>Extracts pixels of one layer inside the selection's coverage.</summary>
    public static SelectionClipboardData Copy(Layer layer, DocumentSelection selection, int docWidth, int docHeight)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(selection);
        var clip = selection.Clip(docWidth, docHeight);
        if (clip.Coverage is null || layer.Pixels is not { } src)
        {
            throw new InvalidOperationException("Nothing selected or layer has no pixels.");
        }

        var outPixels = new byte[clip.Width * clip.Height * 4];
        for (var y = 0; y < clip.Height; y++)
        {
            for (var x = 0; x < clip.Width; x++)
            {
                var factor = clip.Coverage[(y * clip.Width) + x] / 255f;
                if (factor <= 0f)
                {
                    continue;
                }
                var srcIndex = (((clip.Y + y) * docWidth) + clip.X + x) * 4;
                var dstIndex = ((y * clip.Width) + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    outPixels[dstIndex + c] = (byte)MathF.Round(src.Pixels[srcIndex + c] * factor);
                }
            }
        }

        return new SelectionClipboardData { X = clip.X, Y = clip.Y, Width = clip.Width, Height = clip.Height, Pixels = outPixels };
    }

    /// <summary>Copy + clearing the covered pixels to transparent.</summary>
    public static SelectionClipboardData Cut(Document doc, Layer layer, DocumentSelection selection) =>
        MaskedMutate(doc, layer, selection, Copy(layer, selection, doc.Width, doc.Height));

    /// <summary>Alpha-over composites floating pixels onto the layer as one undoable step.</summary>
    public static IUndoCommand CommitFloating(Document doc, Layer layer, FloatingSelection floating)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(floating);
        var patch = new byte[doc.Width * doc.Height * 4];

        for (var y = 0; y < floating.Height; y++)
        {
            var docY = floating.Y + y;
            if (docY < 0 || docY >= doc.Height)
            {
                continue;
            }
            for (var x = 0; x < floating.Width; x++)
            {
                var docX = floating.X + x;
                if (docX < 0 || docX >= doc.Width)
                {
                    continue;
                }
                var srcIndex = ((y * floating.Width) + x) * 4;
                var dstIndex = (((docY * doc.Width) + docX) * 4);
                Array.Copy(floating.Pixels, srcIndex, patch, dstIndex, 4);
            }
        }

        return new BlendPatchCommand(doc, layer, patch, skipTransparent: true);
    }

    private static SelectionClipboardData MaskedMutate(
        Document doc, Layer layer, DocumentSelection selection, SelectionClipboardData data)
    {
        var clip = selection.Clip(doc.Width, doc.Height);
        if (clip.Coverage is null || layer.Pixels is not { } src)
        {
            throw new InvalidOperationException("Nothing selected or layer has no pixels.");
        }

        for (var y = 0; y < clip.Height; y++)
        {
            for (var x = 0; x < clip.Width; x++)
            {
                var factor = clip.Coverage[(y * clip.Width) + x] / 255f;
                if (factor <= 0f)
                {
                    continue;
                }
                var index = (((clip.Y + y) * doc.Width) + clip.X + x) * 4;
                for (var c = 0; c < 3; c++)
                {
                    src.Pixels[index + c] = 0;
                }
                src.Pixels[index + 3] = (byte)MathF.Round(src.Pixels[index + 3] * (1f - factor));
            }
        }

        src.MarkDirty();
        return data;
    }

    /// <summary>
    /// Blends an adjusted full-canvas patch back through the selection:
    /// coverage × patch + (1 − coverage) × original (upstream PixelAdjust.blend).
    /// Returns an undoable command; with no selection the whole canvas updates.
    /// </summary>
    public static IUndoCommand BlendPatch(Document doc, Layer layer, byte[] adjusted, DocumentSelection? selection)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(adjusted);
        var patch = (byte[])adjusted.Clone();

        if (selection is { IsEmpty: false })
        {
            var clip = selection.Clip(doc.Width, doc.Height);
            for (var y = 0; y < doc.Height; y++)
            {
                for (var x = 0; x < doc.Width; x++)
                {
                    var index = ((y * doc.Width) + x) * 4;
                    var factor = clip.FactorAt(x, y);
                    for (var c = 0; c < 4; c++)
                    {
                        patch[index + c] = (byte)MathF.Round(
                            (factor * adjusted[index + c]) + ((1f - factor) * CurrentOrOriginal(layer, index + c)));
                    }
                }
            }
        }

        return new BlendPatchCommand(doc, layer, patch, skipTransparent: false);
    }

    private static byte CurrentOrOriginal(Layer layer, int index) =>
        layer.Pixels is { } surface ? surface.Pixels[index] : (byte)0;

    /// <summary>Undoable replace of layer pixels with a patch (alpha-over merge or masked blend).</summary>
    private sealed class BlendPatchCommand : IUndoCommand
    {
        private readonly Document _doc;
        private readonly Layer _layer;
        private readonly byte[] _patch;
        private readonly bool _skipTransparent;
        private byte[]? _before;

        public BlendPatchCommand(Document doc, Layer layer, byte[] patch, bool skipTransparent)
        {
            _doc = doc;
            _layer = layer;
            _patch = patch;
            _skipTransparent = skipTransparent;
        }

        public void Redo()
        {
            _before ??= _layer.Pixels is { } surface ? (byte[])surface.Pixels.Clone() : null;
            EnsureSurface();
            var pixels = _layer.Pixels!.Pixels;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (_skipTransparent && _patch[i + 3] == 0)
                {
                    continue;
                }
                Array.Copy(_patch, i, pixels, i, 4);
            }
            _layer.Pixels!.MarkDirty();
        }

        public void Undo()
        {
            if (_layer.Pixels is not { } surface)
            {
                return;
            }
            if (_before is not null)
            {
                Array.Copy(_before, surface.Pixels, _before.Length);
            }
            surface.MarkDirty();
        }

        private void EnsureSurface()
        {
            if (_layer.Pixels is null)
            {
                _layer.Pixels = new RasterSurface(_doc.Width, _doc.Height);
                _before = new byte[_doc.Width * _doc.Height * 4];
            }
        }
    }
}
