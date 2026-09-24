using System;
using System.Collections.Generic;
using Compositor.Core.Imaging;
using Compositor.Core.Selection;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Goldens for the content-fill kernel. Every expectation is derived from the algorithm's
/// construction (it copies whole pixels, it needs a fully-known patch to donate from, its
/// generator has a fixed seed), never recorded from a run.
/// </summary>
/// <remarks>
/// The canvas has to be roomy: with a 5x5 patch a donor must sit two pixels from every border
/// and three from the hole, so a small canvas silently has no donors at all and the fill
/// correctly refuses. That geometry is why Size is 15, not 7.
/// </remarks>
public class ContentFillKernelTests
{
    private const int Size = 15;
    private const int Stride = Size * 4;

    private static byte[] Image(Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var rgba = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var i = (y * Stride) + (x * 4);
                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = b;
                rgba[i + 3] = a;
            }
        }

        return rgba;
    }

    private static (byte R, byte G, byte B, byte A) PixelAt(byte[] rgba, int x, int y)
    {
        var i = (y * Stride) + (x * 4);
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    private static SelectionClip Rect(int x, int y, int w, int h) =>
        DocumentSelection.FromShape(SelectionShape.Rectangle(x, y, w, h, SelectionMode.Replace))
            .Clip(Size, Size);

    [Fact]
    public void NothingSelected_IsASuccessAndWritesNothing()
    {
        var rgba = Image((_, _) => (1, 2, 3, 255));
        var before = (byte[])rgba.Clone();
        Assert.True(ContentFill.Fill(rgba, Size, Size, SelectionClip.Empty));
        Assert.Equal(before, rgba);
    }

    [Fact]
    public void EverythingSelected_HasNoDonorAndWritesNothing()
    {
        var rgba = Image((_, _) => (1, 2, 3, 255));
        var before = (byte[])rgba.Clone();
        Assert.False(ContentFill.Fill(rgba, Size, Size, DocumentSelection.All(Size, Size).Clip(Size, Size)));
        Assert.Equal(before, rgba);
    }

    [Fact]
    public void UniformImage_HoleIsFilledWithThatSameColor()
    {
        // Every candidate patch scores zero here, so which donor wins is irrelevant: the only
        // value a copy can produce is the one the whole image already holds.
        var rgba = Image((_, _) => ((byte)10, (byte)20, (byte)30, (byte)255));
        Assert.True(ContentFill.Fill(rgba, Size, Size, Rect(7, 7, 1, 1)));
        var (r, g, b, a) = PixelAt(rgba, 7, 7);
        Assert.Equal(((byte)10, (byte)20, (byte)30, (byte)255), (r, g, b, a));
    }

    [Fact]
    public void FilledPixels_AreCopiesOfWholeOriginalPixelsAndStayInsideTheSelection()
    {
        var palette = new byte[] { 0, 64, 128, 192, 255 };
        var rgba = Image(
            (x, y) => (palette[((x * 3) + y) % 5], palette[(x + (y * 2)) % 5], palette[(x * y) % 5], (byte)255));
        var originals = new HashSet<(byte, byte, byte, byte)>();
        for (var i = 0; i < rgba.Length; i += 4)
        {
            originals.Add((rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]));
        }

        var before = (byte[])rgba.Clone();
        Assert.True(ContentFill.Fill(rgba, Size, Size, Rect(6, 6, 3, 3)));

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var now = PixelAt(rgba, x, y);
                if (x >= 6 && x < 9 && y >= 6 && y < 9)
                {
                    // Inside the hole: whatever landed here came from somewhere real. It can
                    // even coincide with what was there before, because this pattern repeats
                    // every five pixels, so counting changes would prove nothing.
                    Assert.True(originals.Contains(now), $"({x},{y}) invented a pixel that exists nowhere.");
                    Assert.Equal((byte)255, now.A);
                }
                else
                {
                    Assert.Equal(PixelAt(before, x, y), now);
                }
            }
        }
    }

    [Fact]
    public void UnselectedTransparency_IsNeitherSourceNorTarget()
    {
        // Left of x=8 is opaque, the rest is fully transparent, and the hole sits in the
        // transparent half: a ring of nothing. The restart path has to reach a donor for it.
        var rgba = Image(
            (x, _) => x < 8
                ? ((byte)200, (byte)20, (byte)20, (byte)255)
                : ((byte)0, (byte)0, (byte)0, (byte)0));
        Assert.True(ContentFill.Fill(rgba, Size, Size, Rect(11, 7, 1, 1)));

        var (r, _, _, a) = PixelAt(rgba, 11, 7);
        Assert.Equal((byte)255, a);
        Assert.Equal((byte)200, r);

        // Transparency the mask never named stayed transparency.
        Assert.Equal((byte)0, PixelAt(rgba, 14, 0).A);
        Assert.Equal((byte)0, PixelAt(rgba, 9, 9).A);
    }

    [Fact]
    public void SameInputTwice_LandsOnTheSameResult()
    {
        var first = Image(
            (x, y) => ((byte)(x * 15), (byte)(y * 17), (byte)((x + y) * 7), (byte)255));
        var second = (byte[])first.Clone();
        var selection = Rect(6, 6, 3, 3);
        Assert.True(ContentFill.Fill(first, Size, Size, selection));
        Assert.True(ContentFill.Fill(second, Size, Size, selection));
        Assert.Equal(first, second);
    }

    [Fact]
    public void DegenerateArguments_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ContentFill.Fill(new byte[4], 0, 1, SelectionClip.Empty));
        Assert.Throws<ArgumentException>(
            () => ContentFill.Fill(new byte[8], 4, 4, SelectionClip.Empty));
        Assert.Throws<ArgumentNullException>(
            () => ContentFill.Fill(null!, 4, 4, SelectionClip.Empty));
    }
}
