using Compositor.Core;
using Compositor.Core.Project;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Project file (.comp) round-trip and rejection tests, per docs/RESEARCH.md
/// decision 7 and the Mac spec's "every rejection case is a test" rule.
/// </summary>
public sealed class ProjectStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"comp-tests-{Guid.NewGuid():N}");

    public ProjectStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    private static Document BuildRichDocument()
    {
        var bottom = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var top = Guid.NewGuid();
        var doc = new Document(1920, 1080, Guid.NewGuid())
        {
            Name = "Rich doc",
            ActiveLayerId = middle,
        };
        doc.AddLayer(new Layer("bottom", bottom) { Opacity = 1.0, Blend = BlendMode.Normal });
        doc.AddLayer(new Layer("middle", middle)
        {
            Opacity = 0.5,
            Blend = BlendMode.ColorBurn,
            IsVisible = false,
            IsLocked = true,
            Transform = new LayerTransform(10, 20, 800, 600, 15.5, FlipH: true, FlipV: false),
        });
        doc.AddLayer(new Layer("top", top) { Opacity = 0.0, Blend = BlendMode.Difference });
        return doc;
    }

    [Fact]
    public void SaveThenLoad_PreservesEverything()
    {
        var doc = BuildRichDocument();
        var path = PathFor("roundtrip.comp");

        ProjectStore.Save(doc, path);
        var loaded = ProjectStore.Load(path);

        Assert.Equal(doc.Id, loaded.Id);
        Assert.Equal(doc.Name, loaded.Name);
        Assert.Equal(doc.Width, loaded.Width);
        Assert.Equal(doc.Height, loaded.Height);
        Assert.Equal(doc.ActiveLayerId, loaded.ActiveLayerId);
        Assert.Equal(doc.Layers.Count, loaded.Layers.Count);

        for (var i = 0; i < doc.Layers.Count; i++)
        {
            var expected = doc.Layers[i];
            var actual = loaded.Layers[i];
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.IsVisible, actual.IsVisible);
            Assert.Equal(expected.IsLocked, actual.IsLocked);
            Assert.Equal(expected.Opacity, actual.Opacity);
            Assert.Equal(expected.Blend, actual.Blend);
            Assert.Equal(expected.Transform, actual.Transform);
        }

        // Save -> load -> save: manifest bytes identical.
        var path2 = PathFor("roundtrip2.comp");
        ProjectStore.Save(loaded, path2);
        Assert.Equal(File.ReadAllBytes(path), File.ReadAllBytes(path2));
    }

    [Fact]
    public void Save_CreatesValidZipWithManifestEntry()
    {
        var path = PathFor("zip.comp");
        ProjectStore.Save(BuildRichDocument(), path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("manifest.json");
        Assert.NotNull(entry);
        Assert.Single(zip.Entries, e => e.FullName == "manifest.json");
    }

    [Fact]
    public void Save_OverExistingPath_ReplacesAtomically()
    {
        var path = PathFor("atomic.comp");
        ProjectStore.Save(new Document(100, 100), path);
        var firstSize = new FileInfo(path).Length;

        ProjectStore.Save(BuildRichDocument(), path);
        Assert.True(new FileInfo(path).Length > firstSize, "Second save should replace content.");
        Assert.False(File.Exists(path + ".tmp"), "Temp file must be cleaned up.");
    }

    [Fact]
    public void Save_RejectsOversizedCanvas()
    {
        var doc = new Document(30_001, 10);
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Save(doc, PathFor("x.comp")));
    }

    [Fact]
    public void Save_RejectsCanvasOverTotalPixelLimit()
    {
        var doc = new Document(30_000, 30_000); // 900M > 100M total
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Save(doc, PathFor("x.comp")));
    }

    [Fact]
    public void Save_RejectsOpacityOutOfRange()
    {
        var doc = new Document(10, 10);
        doc.AddLayer(new Layer("bad") { Opacity = 1.5 });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Save(doc, PathFor("x.comp")));
    }

    [Fact]
    public void Save_RejectsActiveLayerNotInDocument()
    {
        var doc = new Document(10, 10) { ActiveLayerId = Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Save(doc, PathFor("x.comp")));
    }

    [Fact]
    public void Load_RejectsWrongIdentifier()
    {
        var path = PathFor("bad-id.comp");
        ProjectStore.Save(new Document(10, 10), path);
        RewriteManifest(path, m => m with { Identifier = "com.other.app" });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsNewerVersion()
    {
        var path = PathFor("newer.comp");
        ProjectStore.Save(new Document(10, 10), path);
        RewriteManifest(path, m => m with { Version = 2 });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsUnsafeEntryNames()
    {
        var path = PathFor("unsafe.comp");
        ProjectStore.Save(new Document(10, 10), path);
        InjectEntry(path, "../evil.txt", "nope");
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsDuplicateEntries()
    {
        var path = PathFor("dupe.comp");
        ProjectStore.Save(new Document(10, 10), path);
        InjectEntry(path, "manifest.json", "{}");
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsUnknownBlendMode()
    {
        var path = PathFor("blend.comp");
        var doc = new Document(10, 10);
        doc.AddLayer(new Layer("a"));
        ProjectStore.Save(doc, path);
        RewriteManifest(path, m => m with
        {
            Layers = [m.Layers[0] with { BlendMode = "superMultiply" }],
        });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsMissingImageAsset()
    {
        var path = PathFor("img.comp");
        var doc = new Document(10, 10);
        doc.AddLayer(new Layer("a"));
        ProjectStore.Save(doc, path);
        RewriteManifest(path, m => m with
        {
            Layers = [m.Layers[0] with { Image = $"images/{m.Layers[0].Uuid}.png" }],
        });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsDuplicateLayerUuids()
    {
        var path = PathFor("dupe-uuid.comp");
        var doc = new Document(10, 10);
        doc.AddLayer(new Layer("a"));
        doc.AddLayer(new Layer("b"));
        ProjectStore.Save(doc, path);
        RewriteManifest(path, m => m with
        {
            Layers =
            [
                m.Layers[0],
                m.Layers[1] with { Uuid = m.Layers[0].Uuid },
            ],
        });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsActiveLayerNotPresent()
    {
        var path = PathFor("active.comp");
        ProjectStore.Save(new Document(10, 10), path);
        RewriteManifest(path, m => m with { ActiveLayerUuid = Guid.NewGuid().ToString() });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void Load_RejectsOversizedDimensions()
    {
        var path = PathFor("dim.comp");
        ProjectStore.Save(new Document(10, 10), path);
        RewriteManifest(path, m => m with { Width = 0 });
        Assert.Throws<InvalidOperationException>(() => ProjectStore.Load(path));
    }

    /// <summary>Loads the zip, swaps manifest.json, rewrites the zip.</summary>
    private static void RewriteManifest(string path, Func<Manifest, Manifest> mutate)
    {
        Manifest manifest;
        var otherEntries = new List<(string Name, byte[] Bytes)>();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
            manifest = JsonSerializer.Deserialize<Manifest>(reader.ReadToEnd())!;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName == "manifest.json")
                {
                    continue;
                }

                using var ms = new MemoryStream();
                entry.Open().CopyTo(ms);
                otherEntries.Add((entry.FullName, ms.ToArray()));
            }
        }

        var mutated = mutate(manifest);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(mutated);
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("manifest.json");
            entry.Open().Write(bytes);
            foreach (var (name, data) in otherEntries)
            {
                zip.CreateEntry(name).Open().Write(data);
            }
        }
    }

    private static void InjectEntry(string path, string name, string content)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Update);
        var entry = zip.CreateEntry(name);
        entry.Open().Write(System.Text.Encoding.UTF8.GetBytes(content));
    }
}
