using Compositor.Core;
using Compositor.Core.Selection;
using Xunit;

namespace Compositor.Core.Tests;

public class SelectionTests
{
    private static RasterSurface Surface(int w, int h) => new(w, h);

    [Fact]
    public void Rectangle_CoversExactlyItsBounds()
    {
        var sel = DocumentSelection.FromShape(SelectionShape.Rectangle(2, 1, 3, 2, SelectionMode.Replace));
        var coverage = sel.RenderCoverage(8, 8);

        Assert.Equal(0, coverage[(0 * 8) + 1]);   // outside, above
        Assert.Equal(255, coverage[(1 * 8) + 2]); // inside top-left
        Assert.Equal(255, coverage[(2 * 8) + 4]); // inside bottom-right
        Assert.Equal(0, coverage[(1 * 8) + 5]);   // right of rect (x=5, rect is 2..4)
        Assert.Equal(0, coverage[(3 * 8) + 3]);   // below rect
    }

    [Fact]
    public void Ellipse_ExcludesCornersIncludesCenter()
    {
        var sel = DocumentSelection.FromShape(SelectionShape.Ellipse(0, 0, 10, 10, SelectionMode.Replace));
        var coverage = sel.RenderCoverage(10, 10);

        Assert.Equal(255, coverage[(5 * 10) + 5]); // center fully inside
        Assert.Equal(0, coverage[(0 * 10) + 0]);   // corner outside
        Assert.Equal(0, coverage[(0 * 10) + 9]);   // opposite corner outside
    }

    [Fact]
    public void Lasso_PolygonUsesEvenOddRule()
    {
        var points = new List<(float X, float Y)> { (1, 1), (6, 1), (6, 5), (1, 5) };
        var sel = DocumentSelection.FromShape(SelectionShape.Lasso(points, SelectionMode.Replace));
        var coverage = sel.RenderCoverage(8, 8);

        Assert.Equal(255, coverage[(3 * 8) + 3]); // inside
        Assert.Equal(0, coverage[(0 * 8) + 3]);   // above
        Assert.Equal(0, coverage[(6 * 8) + 3]);   // below
    }

    [Fact]
    public void Add_CombinesBothRegions_Subtract_Removes()
    {
        var a = SelectionShape.Rectangle(0, 0, 4, 4, SelectionMode.Replace);
        var b = SelectionShape.Rectangle(4, 0, 4, 4, SelectionMode.Add);
        var added = DocumentSelection.FromShape(a).Apply(b);
        var coverage = added.RenderCoverage(8, 4);
        Assert.Equal(255, coverage[(1 * 8) + 2]);
        Assert.Equal(255, coverage[(1 * 8) + 6]);

        var hole = SelectionShape.Rectangle(1, 1, 2, 2, SelectionMode.Subtract);
        var cut = added.Apply(hole);
        var after = cut.RenderCoverage(8, 4);
        Assert.Equal(0, after[(2 * 8) + 2]);       // punched out
        Assert.Equal(255, after[(0 * 8) + 2]);     // still selected above
    }

    [Fact]
    public void EmptySelection_TouchesNothing_ClipReturnsNoCoverage()
    {
        var sel = DocumentSelection.FromShape(SelectionShape.Rectangle(0, 0, 1, 1, SelectionMode.Subtract)); // subtract from nothing
        Assert.True(sel.IsEmpty);
        var clip = sel.Clip(4, 4);
        Assert.Null(clip.Coverage);
        Assert.Equal(0f, clip.FactorAt(0, 0));
    }

    [Fact]
    public void Invert_FlipsCoverage()
    {
        var sel = DocumentSelection.FromShape(SelectionShape.Rectangle(0, 0, 2, 2, SelectionMode.Replace));
        var inv = sel.Inverted(4, 4);
        var coverage = inv.RenderCoverage(4, 4);
        Assert.Equal(0, coverage[(0 * 4) + 0]);
        Assert.Equal(255, coverage[(0 * 4) + 2]);
        Assert.Equal(255, coverage[(3 * 4) + 3]);
    }

    [Fact]
    public void SelectAll_ThenInvert_IsEmpty()
    {
        var sel = DocumentSelection.All(6, 5).Inverted(6, 5);
        var clip = sel.Clip(6, 5);
        Assert.Null(clip.Coverage);
    }

    [Fact]
    public void BrushStroke_RespectsSelection()
    {
        var surface = Surface(10, 10);
        var clip = DocumentSelection
            .FromShape(SelectionShape.Rectangle(0, 0, 5, 10, SelectionMode.Replace))
            .Clip(10, 10);

        BrushStroke.Apply(surface, [(2, 5), (8, 5)], radius: 2, 255, 0, 0, 255, 1f, clip);

        // Inside: painted (overlapping dabs saturate coverage to full under the cap).
        Assert.Equal(255, surface.GetPixel(1, 5).A);
        Assert.Equal(0, surface.GetPixel(9, 5).A);  // outside selection
        Assert.Equal(0, surface.GetPixel(7, 5).A);  // stroke reaches here unconstrained; mask blocks it
    }

    [Fact]
    public void MagicWand_FillsContiguousSimilarPixels()
    {
        var surface = Surface(6, 3);
        for (var x = 0; x < 4; x++)
        {
            surface.SetPixel(x, 1, 200, 200, 200, 255); // gray run
        }
        surface.SetPixel(4, 1, 10, 10, 10, 255);        // dark break
        surface.SetPixel(5, 1, 200, 200, 200, 255);     // same gray, not contiguous

        var sel = MagicWand.Select(surface, 0, 1, tolerance: 10, SelectionMode.Replace);
        var coverage = sel.RenderCoverage(6, 3);

        Assert.Equal(255, coverage[(1 * 6) + 0]);
        Assert.Equal(255, coverage[(1 * 6) + 3]);
        Assert.Equal(0, coverage[(1 * 6) + 4]);
        Assert.Equal(0, coverage[(1 * 6) + 5]); // separated by the dark break
    }

    [Fact]
    public void MagicWand_OutOfBounds_IsEmpty()
    {
        var surface = Surface(4, 4);
        Assert.True(MagicWand.Select(surface, -1, 0, 0, SelectionMode.Replace).IsEmpty);
        Assert.True(MagicWand.Select(surface, 4, 0, 0, SelectionMode.Replace).IsEmpty);
    }

    [Fact]
    public void CopyCutPaste_RoundTripsPixelsThroughMask()
    {
        var doc = new Document(8, 8);
        var layer = new Layer("l") { Pixels = new RasterSurface(8, 8) };
        for (var y = 2; y < 5; y++)
        {
            for (var x = 2; x < 5; x++)
            {
                layer.Pixels!.SetPixel(x, y, 255, 128, 0, 255);
            }
        }
        doc.AddLayer(layer);

        var sel = DocumentSelection.FromShape(SelectionShape.Rectangle(2, 2, 2, 2, SelectionMode.Replace)); // 2x2 of the 3x3 block
        var copied = SelectionOps.Copy(layer, sel, doc.Width, doc.Height);

        Assert.Equal(2, copied.Width);
        Assert.Equal(255, copied.Pixels[0]);          // red channel of first covered pixel
        Assert.Equal(255, copied.Pixels[3]);          // fully covered -> opaque

        var floating = new FloatingSelection(copied.Pixels, 5, 5, copied.Width, copied.Height);
        floating.Move(1, 0);
        var command = SelectionOps.CommitFloating(doc, layer, floating);
        command.Redo();

        Assert.Equal((255, 128, 0, 255), layer.Pixels!.GetPixel(6, 5));
        Assert.Equal((255, 128, 0, 255), layer.Pixels!.GetPixel(2, 2)); // original intact

        command.Undo();
        Assert.Equal((0, 0, 0, 0), layer.Pixels!.GetPixel(6, 5));
    }

    [Fact]
    public void Cut_ClearsCoveredPixelsOnly()
    {
        var doc = new Document(6, 6);
        var layer = new Layer("l") { Pixels = new RasterSurface(6, 6) };
        layer.Pixels!.SetPixel(1, 1, 0, 255, 0, 255);
        layer.Pixels.SetPixel(4, 4, 0, 0, 255, 255);
        doc.AddLayer(layer);

        var sel = DocumentSelection.FromShape(SelectionShape.Rectangle(0, 0, 2, 2, SelectionMode.Replace));
        var data = SelectionOps.Cut(doc, layer, sel);

        Assert.Equal((0, 0, 0, 0), layer.Pixels.GetPixel(1, 1));    // cut away
        Assert.Equal((0, 0, 255, 255), layer.Pixels.GetPixel(4, 4)); // untouched
        Assert.Equal(2, data.Width);
    }

    [Fact]
    public void BlendPatch_MergesThroughSelectionOnly()
    {
        var doc = new Document(4, 4);
        var layer = new Layer("l") { Pixels = new RasterSurface(4, 4) };
        doc.AddLayer(layer);

        var adjusted = new byte[4 * 4 * 4];
        for (var i = 0; i < adjusted.Length; i += 4)
        {
            adjusted[i] = 255;         // red everywhere
            adjusted[i + 3] = 255;
        }
        var sel = DocumentSelection.FromShape(SelectionShape.Rectangle(0, 0, 2, 4, SelectionMode.Replace));

        var command = SelectionOps.BlendPatch(doc, layer, adjusted, sel);
        command.Redo();

        Assert.Equal((255, 0, 0, 255), layer.Pixels!.GetPixel(0, 2)); // selected half
        Assert.Equal((0, 0, 0, 0), layer.Pixels!.GetPixel(3, 2));     // outside stays empty
    }
}
