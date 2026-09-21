using Compositor.Core;
using Compositor.Core.Commands;
using Compositor.Core.Imaging;
using Compositor.Core.Project;
using Compositor.Core.Selection;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>Layer power parity (task 5): groups, merge, flip, transform math.</summary>
public sealed class LayerPowerTests
{
    private static Layer PixelLayer(string name, int w = 4, int h = 4, byte alpha = 255)
    {
        var surface = new RasterSurface(w, h);
        for (var i = 3; i < surface.Pixels.Length; i += 4)
        {
            surface.Pixels[i] = alpha;
        }
        surface.MarkDirty();
        return new Layer(name) { Pixels = surface };
    }

    // ---------- hierarchy ----------

    [Fact]
    public void Entries_OrderParentBeforeChildren_WithDepthAndVisibility()
    {
        var doc = new Document(10, 10);
        var bottom = PixelLayer("bottom");
        var group = Layer.Group("Folder 1");
        var childA = PixelLayer("a");
        var childB = PixelLayer("b");
        doc.Layers.AddRange([bottom, group, childA, childB]);
        childA.ParentId = group.Id;
        childB.ParentId = group.Id;

        var entries = LayerHierarchy.Entries(doc.Layers);

        Assert.Equal(
            [bottom, group, childA, childB],
            entries.Select(e => e.Layer).ToList());
        Assert.Equal([0, 0, 1, 1], entries.Select(e => e.Depth).ToList());
        Assert.All(entries, e => Assert.True(e.Visible));
    }

    [Fact]
    public void Entries_GroupVisibility_CascadesToChildren()
    {
        var doc = new Document(10, 10);
        var group = Layer.Group("g");
        var child = PixelLayer("c");
        child.ParentId = group.Id;
        doc.Layers.AddRange([group, child]);
        group.IsVisible = false;

        var entries = LayerHierarchy.Entries(doc.Layers);

        Assert.False(entries.Single(e => e.Layer == group).Visible);
        Assert.False(entries.Single(e => e.Layer == child).Visible);
        Assert.Empty(LayerHierarchy.VisibleLayers(doc.Layers));
    }

    [Fact]
    public void Validate_RejectsNonGroupParent_MissingParent_GroupWithPixels_AndCycles()
    {
        // non-group parent
        var doc = new Document(10, 10);
        var plain = PixelLayer("p");
        var child = PixelLayer("c");
        child.ParentId = plain.Id;
        doc.Layers.AddRange([plain, child]);
        Assert.Throws<InvalidOperationException>(() => LayerHierarchy.Validate(doc.Layers));

        // missing parent
        var doc2 = new Document(10, 10);
        var orphan = PixelLayer("orphan");
        orphan.ParentId = Guid.NewGuid();
        doc2.Layers.Add(orphan);
        Assert.Throws<InvalidOperationException>(() => LayerHierarchy.Validate(doc2.Layers));

        // group with pixels
        var doc3 = new Document(10, 10);
        var bad = PixelLayer("bad");
        bad.IsGroup = true;
        doc3.Layers.Add(bad);
        Assert.Throws<InvalidOperationException>(() => LayerHierarchy.Validate(doc3.Layers));

        // self-parent cycle
        var doc4 = new Document(10, 10);
        var g1 = Layer.Group("g1");
        var g2 = Layer.Group("g2");
        g1.ParentId = g2.Id;
        g2.ParentId = g1.Id;
        doc4.Layers.AddRange([g1, g2]);
        Assert.Throws<InvalidOperationException>(() => LayerHierarchy.Validate(doc4.Layers));
    }

    [Fact]
    public void DescendantIds_FindsNestedChildren()
    {
        var doc = new Document(10, 10);
        var outer = Layer.Group("outer");
        var inner = Layer.Group("inner");
        var leaf = PixelLayer("leaf");
        inner.ParentId = outer.Id;
        leaf.ParentId = inner.Id;
        doc.Layers.AddRange([outer, inner, leaf]);

        var descendants = LayerHierarchy.DescendantIds(doc.Layers, outer.Id);

        Assert.Equal(2, descendants.Count);
        Assert.Contains(inner.Id, descendants);
        Assert.Contains(leaf.Id, descendants);
    }

    // ---------- group command ----------

    [Fact]
    public void GroupLayersCommand_UndoRestoresParentsAndOrder()
    {
        var doc = new Document(10, 10);
        var a = PixelLayer("a");
        var b = PixelLayer("b");
        doc.Layers.AddRange([a, b]);

        var history = new UndoHistory();
        history.Push(new GroupLayersCommand(doc, [a, b]));

        Assert.Equal(3, doc.Layers.Count);
        var group = doc.Layers[0];
        Assert.True(group.IsGroup);
        Assert.Equal("Folder 1", group.Name);
        Assert.Equal([group, a, b], doc.Layers);
        Assert.Equal(group.Id, a.ParentId);
        Assert.Equal(group.Id, b.ParentId);

        history.Undo();

        Assert.Equal([a, b], doc.Layers);
        Assert.Null(a.ParentId);
        Assert.Null(b.ParentId);
    }

    [Fact]
    public void GroupLayersCommand_NamesFirstFreeFolder()
    {
        var doc = new Document(10, 10);
        doc.Layers.Add(Layer.Group("Folder 1"));
        doc.Layers.Add(Layer.Group("Folder 2"));
        var a = PixelLayer("a");
        doc.Layers.Add(a);

        new GroupLayersCommand(doc, [a]).Redo();

        Assert.Equal("Folder 3", doc.Layers.Single(l => l.IsGroup && l.Name.StartsWith("Folder 3", StringComparison.Ordinal)).Name);
    }

    // ---------- reorder + appearance ----------

    [Fact]
    public void ReorderLayerCommand_MovesAndRestores()
    {
        var doc = new Document(10, 10);
        var a = PixelLayer("a");
        var b = PixelLayer("b");
        var c = PixelLayer("c");
        doc.Layers.AddRange([a, b, c]);

        var history = new UndoHistory();
        history.Push(new ReorderLayerCommand(doc, a, 2));

        Assert.Equal([b, c, a], doc.Layers);
        history.Undo();
        Assert.Equal([a, b, c], doc.Layers);
    }

    [Fact]
    public void SetLayerAppearanceCommand_RestoresBlendAndOpacity()
    {
        var layer = PixelLayer("x");
        var history = new UndoHistory();
        history.Push(new SetLayerAppearanceCommand(layer, BlendMode.Multiply, 0.5));

        Assert.Equal(BlendMode.Multiply, layer.Blend);
        Assert.Equal(0.5, layer.Opacity);
        history.Undo();
        Assert.Equal(BlendMode.Normal, layer.Blend);
        Assert.Equal(1.0, layer.Opacity);
    }

    [Fact]
    public void SetLayerAppearanceCommand_RejectsInvalidOpacity()
    {
        var layer = PixelLayer("x");
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SetLayerAppearanceCommand(layer, null, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SetLayerAppearanceCommand(layer, null, double.NaN));
    }

    // ---------- merge ----------

    [Fact]
    public void MergeLayersCommand_BakesBlendOpacity_Trims_AndUndoRestores()
    {
        var doc = new Document(4, 4);
        var bottom = PixelLayer("bottom");
        // bottom: red pixel at (0,0) only
        bottom.Pixels!.SetPixel(0, 0, 200, 0, 0, 255);
        var top = PixelLayer("top");
        // top: 50% gray over full canvas at 50% opacity, multiply over backdrop.
        // Where backdrop alpha is 0 (everywhere except (0,0)), result keeps bottom's pixels.
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                top.Pixels!.SetPixel(x, y, 128, 128, 128, 255);
            }
        }
        top.Opacity = 0.5;
        top.Blend = BlendMode.Normal;
        doc.Layers.AddRange([bottom, top]);

        var history = new UndoHistory();
        var cmd = new MergeLayersCommand(doc, [bottom, top]);
        history.Push(cmd);

        var merged = Assert.Single(doc.Layers);
        Assert.Equal("bottom", merged.Name);
        // Content bounds: bottom only has an opaque pixel at (0,0); top contributes
        // alpha over alpha-0 backdrop only where it is drawn (everywhere), so the
        // composited buffer is opaque across the full canvas (source-over at 50%
        // over transparent = 127.5 -> 128 alpha). Trim keeps the full 4x4.
        Assert.Equal(4, merged.Pixels!.Width);
        Assert.Equal(4, merged.Pixels.Height);
        // Top-left: 50% gray over opaque red at 50% opacity = blend of (128,128,128) and (200,0,0)
        var (r, g, b, a) = merged.Pixels.GetPixel(0, 0);
        Assert.Equal(255, a);
        Assert.Equal(164, r); // (128*0.5 + 200*0.5) = 164
        Assert.Equal(64, g); // (128*0.5 + 0*0.5) = 64
        Assert.Equal(64, b);
        Assert.Equal(0, merged.Transform.OriginX);
        Assert.Equal(0, merged.Transform.OriginY);

        history.Undo();
        Assert.Equal([bottom, top], doc.Layers);
        history.Redo();
        Assert.Equal(merged, Assert.Single(doc.Layers));
    }

    [Fact]
    public void MergeLayersCommand_TrimsToContentBounds()
    {
        var doc = new Document(8, 8);
        var bottom = PixelLayer("b", 8, 8);
        var top = PixelLayer("t", 8, 8);
        top.Pixels!.Clear(); // make top fully transparent first
        // A single blue pixel at (6,7) on top
        top.Pixels.SetPixel(6, 7, 0, 0, 255, 255);
        // bottom fully transparent
        bottom.Pixels!.Clear();
        doc.Layers.AddRange([bottom, top]);

        new MergeLayersCommand(doc, [bottom, top]).Redo();

        var merged = Assert.Single(doc.Layers);
        Assert.Equal(1, merged.Pixels!.Width);
        Assert.Equal(1, merged.Pixels.Height);
        Assert.Equal(6, merged.Transform.OriginX);
        Assert.Equal(7, merged.Transform.OriginY);
        var (r, g, b, a) = merged.Pixels.GetPixel(0, 0);
        Assert.Equal((byte)0, r);
        Assert.Equal((byte)0, g);
        Assert.Equal((byte)255, b);
        Assert.Equal((byte)255, a);
    }

    [Fact]
    public void MergeLayersCommand_RejectsBlankLayers()
    {
        var doc = new Document(4, 4);
        var blank = new Layer("blank");
        var solid = PixelLayer("s");
        doc.Layers.AddRange([blank, solid]);
        Assert.Throws<ArgumentException>(() => new MergeLayersCommand(doc, [blank, solid]));
    }

    // ---------- flip ----------

    [Fact]
    public void FlipLayerCommand_MirrorsPixels_KeepsAlpha_UndoRestores()
    {
        var layer = PixelLayer("f");
        layer.Pixels!.Clear();
        layer.Pixels.SetPixel(0, 0, 255, 0, 0, 255); // red dot top-left

        var history = new UndoHistory();
        history.Push(new FlipLayerCommand(layer, horizontally: true));

        var (r, g, b, a) = layer.Pixels!.GetPixel(3, 0);
        Assert.Equal((byte)255, r);
        Assert.Equal((byte)255, a);
        Assert.True(layer.Transform.FlipH);

        history.Undo();
        var (r0, g0, b0, a0) = layer.Pixels!.GetPixel(0, 0);
        Assert.Equal((byte)255, r0);
        Assert.Equal((byte)255, a0);
        Assert.False(layer.Transform.FlipH);
    }

    [Fact]
    public void FlipLayerCommand_VerticalMirrorsRows()
    {
        var layer = PixelLayer("f");
        layer.Pixels!.Clear();
        layer.Pixels.SetPixel(1, 0, 0, 255, 0, 255);

        new FlipLayerCommand(layer, horizontally: false).Redo();

        var (r, g, _, _) = layer.Pixels!.GetPixel(1, 3);
        Assert.Equal((byte)0, r);
        Assert.Equal((byte)255, g);
        Assert.True(layer.Transform.FlipV);
    }

    [Fact]
    public void FlipCanvasCommand_FlipsAllLayersAndSelection()
    {
        var doc = new Document(4, 4);
        var a = PixelLayer("a");
        var b = PixelLayer("b");
        a.Pixels!.Clear();
        a.Pixels.SetPixel(0, 0, 255, 0, 0, 255);
        b.Pixels!.Clear();
        b.Pixels.SetPixel(3, 3, 0, 0, 255, 255);
        doc.Layers.AddRange([a, b]);
        doc.Selection = DocumentSelection.FromShape(
            SelectionShape.Rectangle(0, 0, 2, 2, SelectionMode.Replace));

        var history = new UndoHistory();
        history.Push(new FlipCanvasCommand(doc, horizontally: true));

        Assert.Equal((byte)255, a.Pixels!.GetPixel(3, 0).R);
        Assert.Equal((byte)255, b.Pixels!.GetPixel(0, 3).B);
        var shape = Assert.Single(doc.Selection!.Shapes);
        Assert.Equal(2f, shape.X); // 4 - (0+2) = 2

        history.Undo();
        Assert.Equal((byte)255, a.Pixels!.GetPixel(0, 0).R);
        Assert.Equal(0f, doc.Selection!.Shapes.Single().X);
    }

    // ---------- transform math (upstream parity) ----------

    [Fact]
    public void Transform_Scaled_KeepsCenterAndRoundsClean()
    {
        var t = new LayerTransform(10, 20, 100, 50, 0, false, false);
        var scaled = t.Scaled(200, pixelWidth: 100, pixelHeight: 50);

        Assert.Equal(60, scaled.CenterX);
        Assert.Equal(45, scaled.CenterY);
        Assert.Equal(200, scaled.Width);
        Assert.Equal(100, scaled.Height);
    }

    [Fact]
    public void Transform_Mirrored_MatchesUpstreamFormula()
    {
        // upstream: origin = 2*axis - center - size/2, rotation negates, flip toggles
        var t = new LayerTransform(0, 0, 100, 40, 30, false, false);
        var mirrored = t.Mirrored(horizontally: true, axis: 200);

        Assert.Equal(2 * 200 - 50 - 50, mirrored.OriginX); // center.x=50
        Assert.Equal(0, mirrored.OriginY);
        Assert.Equal(-30, mirrored.RotationDegrees);
        Assert.True(mirrored.FlipH);
        Assert.False(mirrored.FlipV);
    }

    [Fact]
    public void Transform_IsValid_RejectsNonFiniteAndHuge()
    {
        Assert.True(new LayerTransform(0, 0, 100, 100, 0, false, false).IsValid);
        Assert.True(default(LayerTransform).IsValid); // cover-canvas blank layers
        Assert.False(new LayerTransform(double.NaN, 0, 100, 100, 0, false, false).IsValid);
        Assert.False(new LayerTransform(0, 0, 400_000, 100, 0, false, false).IsValid);
        Assert.False(new LayerTransform(2_000_000, 0, 100, 100, 0, false, false).IsValid);
    }

    // ---------- project round-trip with groups ----------

    [Fact]
    public void ProjectStore_GroupsRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "comp-layertests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"groups-{Guid.NewGuid():N}.comp");

        var doc = new Document(4, 4);
        var bottom = PixelLayer("bottom");
        var group = Layer.Group("Folder 1");
        var child = PixelLayer("child");
        child.ParentId = group.Id;
        doc.Layers.AddRange([bottom, group, child]);
        doc.ActiveLayerId = child.Id;

        ProjectStore.Save(doc, path);
        var loaded = ProjectStore.Load(path);

        Assert.Equal(3, loaded.Layers.Count);
        Assert.True(loaded.Layers[1].IsGroup);
        Assert.Null(loaded.Layers[1].Pixels);
        Assert.Equal(loaded.Layers[1].Id, loaded.Layers[2].ParentId);
        Assert.Equal(child.Id, loaded.ActiveLayerId);
        Assert.Equal(2, ProjectStore.Version);
    }

    [Fact]
    public void ProjectStore_LoadRejectsGroupWithImage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "comp-layertests");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"badgroup-{Guid.NewGuid():N}.comp");

        var doc = new Document(4, 4);
        doc.Layers.Add(PixelLayer("solid"));
        ProjectStore.Save(doc, path);

        // Rewrite the manifest so the pixel layer claims to be a group.
        string json;
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            using var zip = new System.IO.Compression.ZipArchive(
                stream, System.IO.Compression.ZipArchiveMode.Update);
            var entry = zip.GetEntry("manifest.json")!;
            using (var reader = new StreamReader(entry.Open()))
            {
                json = reader.ReadToEnd();
            }
            entry.Delete();
            var fresh = zip.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(fresh.Open()))
            {
                writer.Write(json.Replace("\"isGroup\": false", "\"isGroup\": true"));
            }
        }

        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }
}
