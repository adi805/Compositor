using Compositor.Core;
using Compositor.Core.Imaging;
using Compositor.Core.Project;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>PNG codec and pixel-bearing .comp round-trip.</summary>
public sealed class PngRoundTripTests
{
    [Fact]
    public void EncodeThenDecode_PreservesPixelsExactly()
    {
        var surface = new RasterSurface(32, 16);
        // Pattern exercising several channel + alpha values, incl. zero alpha.
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                surface.SetPixel(x, y, (byte)(x * 8), (byte)(y * 16), (byte)((x * y) % 256), (byte)((x + y) % 256));
            }
        }

        using var ms = new MemoryStream();
        Png.Encode(ms, surface.Width, surface.Height, surface.Pixels);
        ms.Position = 0;
        var (w, h, rgba) = Png.Decode(ms);

        Assert.Equal(32, w);
        Assert.Equal(16, h);
        Assert.Equal(surface.Pixels, rgba);
    }

    [Fact]
    public void Decode_RejectsNonPng()
    {
        Assert.Throws<InvalidDataException>(() => Png.Decode(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8])));
    }

    [Fact]
    public void Decode_RejectsCorruptedCrc()
    {
        var surface = new RasterSurface(4, 4);
        using var ms = new MemoryStream();
        Png.Encode(ms, surface.Width, surface.Height, surface.Pixels);
        var bytes = ms.ToArray();
        bytes[^2] ^= 0xFF; // corrupt IEND CRC
        Assert.Throws<InvalidDataException>(() => Png.Decode(new MemoryStream(bytes)));
    }

    [Fact]
    public void ProjectRoundTrip_PreservesPixelData()
    {
        var doc = new Document(64, 64);
        var painted = new Layer("painted")
        {
            Pixels = new RasterSurface(64, 64),
        };
        BrushStroke.Apply(
            painted.Pixels,
            [(10f, 10f), (50f, 50f)],
            radius: 6, 200, 30, 90, 255, 1f);
        doc.AddLayer(new Layer("blank"));
        doc.AddLayer(painted);

        var path = Path.Combine(Path.GetTempPath(), $"comp-png-{Guid.NewGuid():N}.comp");
        try
        {
            ProjectStore.Save(doc, path);
            var loaded = ProjectStore.Load(path);

            var originalBytes = doc.Layers[1].Pixels!.Pixels;
            var loadedLayer = loaded.Layers[1];
            Assert.NotNull(loadedLayer.Pixels);
            Assert.Equal(64, loadedLayer.Pixels.Width);
            Assert.Equal(64, loadedLayer.Pixels.Height);
            Assert.Equal(originalBytes, loadedLayer.Pixels.Pixels);
            Assert.Null(loaded.Layers[0].Pixels); // blank layer stays blank

            // Re-save must keep byte-identical manifest AND pixel-identical assets.
            var path2 = path + "2";
            ProjectStore.Save(loaded, path2);
            var loaded2 = ProjectStore.Load(path2);
            Assert.Equal(loadedLayer.Pixels.Pixels, loaded2.Layers[1].Pixels!.Pixels);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + "2");
        }
    }
}
