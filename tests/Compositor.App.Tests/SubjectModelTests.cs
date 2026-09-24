using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// Proof that the ML path is real on Windows, not a promise.
///
/// Upstream's "Remove Background" leans on Apple Vision, which has no Windows build. The
/// replacement decided in docs/PARITY.md (WS12) is a U2-Net saliency model run through
/// ONNX Runtime: Apache-2.0 weights, ~4.5 MB, CPU-only. This file only establishes the
/// mechanism end to end: the pinned weights load, the graph runs, and it produces a
/// spatially varying mask.
///
/// Deliberately NOT claimed here: that mask quality matches Apple Vision. That needs real
/// photographs scored against something, and it is written up as an open item rather than
/// graded by eye.
/// </summary>
public class SubjectModelTests
{
    /// <summary>Pinned download; the digest is the contract, re-download if it ever moves.</summary>
    private const string ModelFile = "u2netp.onnx";

    private const long ModelBytes = 4_574_861;

    private const string ModelSha256 = "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8";

    private const int Side = 320;

    private static string ModelPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "assets", "models", ModelFile);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"{ModelFile} not found walking up from {AppContext.BaseDirectory}. Fetch it with: " +
            "curl -L -o assets/models/u2netp.onnx " +
            "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx");
    }

    [Fact]
    public void Weights_AreThePinnedDownload()
    {
        Assert.Equal(ModelBytes, new FileInfo(ModelPath()).Length);
        using var stream = File.OpenRead(ModelPath());
        var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        Assert.Equal(ModelSha256, actual);
    }

    [Fact]
    public void Session_LoadsASingleImageTensorAtThisSize()
    {
        using var session = new InferenceSession(ModelPath());
        var meta = session.InputMetadata;
        var name = Assert.Single(meta.Keys);
        var dims = meta[name].Dimensions;
        Assert.Equal(4, dims.Length);
        // Batch and channels are fixed and the sides are 320 for u2netp. A -1 anywhere here
        // would mean a dynamic axis, and the caller would have to choose a size itself.
        Assert.Equal(1, dims[0]);
        Assert.Equal(3, dims[1]);
        Assert.All(dims.Skip(2), d => Assert.Equal(Side, d));
        Assert.NotEmpty(session.OutputNames);
    }

    /// <summary>
    /// ImageNet-style mean/std normalisation over three channels, CHW. The model was trained
    /// on this transform, so anything else is a different function.
    /// </summary>
    private static float[] Blob(byte[] rgb, int width, int height)
    {
        var data = new float[3 * Side * Side];
        float[] mean = { 0.485f, 0.456f, 0.406f };
        float[] std = { 0.229f, 0.224f, 0.225f };
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                // Nearest-sample the source up to the network's size.
                var sx = Math.Min(width - 1, (x * width) / Side);
                var sy = Math.Min(height - 1, (y * height) / Side);
                var src = (sy * width + sx) * 3;
                for (var c = 0; c < 3; c++)
                {
                    data[(c * Side * Side) + (y * Side) + x] = (rgb[src + c] / 255f - mean[c]) / std[c];
                }
            }
        }

        return data;
    }

    private static float[] Run(InferenceSession session, string inputName, int[] dims, float[] data)
    {
        var tensor = new DenseTensor<float>(data, dims);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, tensor) };
        using var results = session.Run(inputs);
        return results[0].AsTensor<float>().ToArray();
    }

    [Fact]
    public void Inference_ProducesAFiniteSpatialMask()
    {
        using var session = new InferenceSession(ModelPath());
        var inputName = session.InputMetadata.Keys.Single();
        var dims = session.InputMetadata[inputName].Dimensions;

        // A distinct subject on a flat ground: the case the upstream command targets.
        var w = 64;
        var h = 64;
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 3;
                var subject = x >= 20 && x < 44 && y >= 16 && y < 50;
                rgb[i] = (byte)(subject ? 210 : 20);
                rgb[i + 1] = (byte)(subject ? 60 : 20);
                rgb[i + 2] = (byte)(subject ? 40 : 20);
            }
        }

        var mask = Run(session, inputName, dims, Blob(rgb, w, h));
        Assert.NotEmpty(mask);
        Assert.All(mask, v => Assert.True(float.IsFinite(v), $"non-finite output {v}"));
        Assert.All(mask, v => Assert.InRange(v, -0.01f, 1.01f));

        // A constant answer would mean the graph collapsed or the binding is wrong: a real
        // saliency map varies across the image.
        var spread = mask.Max() - mask.Min();
        Assert.True(spread > 0.05f, $"mask is flat (spread {spread})");
    }

    [Fact]
    public void Inference_FlatInputDoesNotManufactureStructure()
    {
        using var session = new InferenceSession(ModelPath());
        var inputName = session.InputMetadata.Keys.Single();
        var dims = session.InputMetadata[inputName].Dimensions;

        var grey = new byte[64 * 64 * 3];
        Array.Fill(grey, (byte)128);
        var greySpread = Spread(Run(session, inputName, dims, Blob(grey, 64, 64)));

        var white = new byte[64 * 64 * 3];
        Array.Fill(white, (byte)255);
        var whiteSpread = Spread(Run(session, inputName, dims, Blob(white, 64, 64)));

        // With no subject there is nothing to outline, so the map should stay near-constant.
        // The bound is generous on purpose: this asks "does it hallucinate an edge", not
        // "is it exactly 0.5".
        Assert.True(greySpread < 0.5f, $"uniform grey produced spread {greySpread}");
        Assert.True(whiteSpread < 0.5f, $"uniform white produced spread {whiteSpread}");
    }

    private static float Spread(float[] values) => values.Max() - values.Min();
}
