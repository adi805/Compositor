using Compositor.Core.Update;
using Xunit;

namespace Compositor.Core.Tests;

/// <summary>
/// Versions and release manifests. No network: the JSON below is the real shape of a
/// <c>/releases/latest</c> response and the checksum text is real <c>sha256sum</c> output, so the parser
/// is tested against what the API actually sends rather than against what I assumed it sends.
/// </summary>
public sealed class AppVersionTests
{
    private const string ReleaseJson = """
    {
      "tag_name": "v0.4.0",
      "name": "Compositor.Windows v0.4.0",
      "prerelease": false,
      "draft": false,
      "html_url": "https://github.com/adi805/Compositor-Windows/releases/tag/v0.4.0",
      "assets": [
        {
          "name": "Compositor-Windows-v0.4.0-win-x64.zip",
          "browser_download_url": "https://objects.example.com/win64.zip",
          "content_type": "application/zip",
          "size": 43221944,
          "state": "uploaded"
        },
        {
          "name": "SHA256SUMS",
          "browser_download_url": "https://objects.example.com/sums.txt",
          "content_type": "application/octet-stream",
          "size": 104,
          "state": "uploaded"
        }
      ]
    }
    """;

    private const string KnownHash = "e1619a50b24caac5a7730059d6340eb31346385d59c6c155356e206de6061471";

    private const string Checksums = KnownHash + "  Compositor-Windows-v0.4.0-win-x64.zip\n";

    [Fact]
    public void ParsesPlainAndTagForms()
    {
        Assert.True(AppVersion.TryParse("0.3.0", out var plain));
        Assert.Equal(0, plain.Major);
        Assert.Equal(3, plain.Minor);
        Assert.Equal(0, plain.Patch);
        Assert.True(plain.IsRelease);

        Assert.True(AppVersion.TryParse("v0.3.0", out var tagged));
        Assert.Equal(plain, tagged);

        // A tag may leave off the patch; that is the same version, not an unknown one.
        Assert.True(AppVersion.TryParse("1.2", out var shortVersion));
        Assert.Equal(new AppVersion(1, 2, 0), shortVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-version")]
    [InlineData("1.2.3.4")]
    [InlineData("-1.0.0")]
    [InlineData("1.x.3")]
    public void RejectsWhatItCannotRead(string text) => Assert.False(AppVersion.TryParse(text, out _));

    [Theory]
    [InlineData("0.10.0", "0.9.0", true)] // numeric, not lexical: string compare gets this one backwards
    [InlineData("0.9.0", "0.10.0", false)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("0.3.0", "0.3.0", false)] // the same version is never an update
    [InlineData("v0.4.0", "0.3.0", true)] // a tag prefix must not break the comparison
    [InlineData("0.4.0", "0.4.0-beta.1", true)] // a prerelease is a draft of its release
    [InlineData("0.4.0-beta.1", "0.4.0", false)] // and is therefore never offered over it
    [InlineData("garbage", "0.3.0", false)] // unreadable means "do not offer", not "assume newer"
    [InlineData("0.4.0", "garbage", false)]
    public void IsNewer_ComparesByComponent(string candidate, string installed, bool expected) =>
        Assert.Equal(expected, AppVersion.IsNewer(candidate, installed));

    [Fact]
    public void PrereleaseLabelIsKept()
    {
        Assert.True(AppVersion.TryParse("0.4.0-beta.1", out var beta));
        Assert.Equal("beta.1", beta.Prerelease);
        Assert.False(beta.IsRelease);
        Assert.Equal("0.4.0-beta.1", beta.ToString());
        Assert.Equal("0.4.0", new AppVersion(0, 4, 0).ToString());
    }

    [Fact]
    public void ParsesReleaseZipAndChecksumAsset()
    {
        var release = ReleaseManifest.Parse(ReleaseJson);

        Assert.NotNull(release);
        Assert.Equal("v0.4.0", release!.Value.Tag);
        Assert.Equal(new AppVersion(0, 4, 0), release.Value.Version);
        Assert.False(release.Value.IsPrerelease);
        Assert.Equal("Compositor-Windows-v0.4.0-win-x64.zip", release.Value.AssetName);
        Assert.Equal("https://objects.example.com/win64.zip", release.Value.DownloadUrl);
        Assert.Equal(43_221_944L, release.Value.AssetSizeBytes);
        Assert.Equal("https://objects.example.com/sums.txt", release.Value.ChecksumsUrl);

        // Not downloadable until the hash has been read: the zip on its own proves nothing.
        Assert.False(release.Value.IsDownloadable);
    }

    [Fact]
    public void ResolvesTheHashForTheAssetItNames()
    {
        var resolved = ReleaseManifest.Resolve(ReleaseManifest.Parse(ReleaseJson), Checksums);

        Assert.NotNull(resolved);
        Assert.Equal(KnownHash, resolved!.Value.Sha256);
        Assert.True(resolved.Value.IsDownloadable);
        Assert.True(ReleaseManifest.Verify(resolved.Value.Sha256, KnownHash.ToUpperInvariant())); // either case
    }

    [Fact]
    public void AManifestThatDoesNotNameTheAssetRefusesTheRelease()
    {
        // Same file, but the hash line names a different build: nothing here says which hash to trust, so
        // the answer is no download rather than an unverifiable one.
        var mismatched = Checksums.Replace("v0.4.0", "v0.3.0");

        Assert.Null(ReleaseManifest.Resolve(ReleaseManifest.Parse(ReleaseJson), mismatched));
    }

    [Fact]
    public void IgnoresSourceArchivesAndKeepsOnlyTheWindowsZip()
    {
        // A real release also carries auto-generated source archives; staging one of those over the app
        // would replace an executable with a tree of .cs files.
        const string Json = """
        {
          "tag_name": "v0.5.0",
          "prerelease": false,
          "html_url": "https://example.invalid/v0.5.0",
          "assets": [
            { "name": "Compositor-Windows-0.5.0.zip", "browser_download_url": "https://example.invalid/src.zip" },
            { "name": "Compositor-Windows-v0.5.0-win-x64.zip", "browser_download_url": "https://example.invalid/app.zip" }
          ]
        }
        """;

        var release = ReleaseManifest.Parse(Json);
        Assert.Equal("Compositor-Windows-v0.5.0-win-x64.zip", release?.AssetName);
    }

    [Fact]
    public void AReleaseWithNoWindowsAssetIsNotAnUpdate()
    {
        const string Json = """
        { "tag_name": "v9.9.9", "prerelease": false, "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.invalid/notes.txt" } ] }
        """;

        Assert.Null(ReleaseManifest.Parse(Json));
    }

    [Fact]
    public void ChecksumLinesTolerateTheRealOutputFormats()
    {
        // Hashes built with new string(c, 64) so "64 hex digits" is a fact about the code, not about how
        // well I counted characters by eye.
        var a = new string('a', 64);
        var b = new string('b', 64);
        var text = string.Join(
            '\n',
            "# a comment line, too short to be a record",
            $"{a}  first.zip",
            $"{b} *binary-mode.zip",
            string.Empty,
            $"{new string('c', 60)}  short-hex.zip",
            $"{new string('d', 63)}z  not-hex.zip") + "\n";

        var parsed = ReleaseManifest.ParseChecksums(text);

        Assert.Equal(a, parsed["first.zip"]);
        Assert.Equal(b, parsed["binary-mode.zip"]);
        Assert.False(parsed.ContainsKey("short-hex.zip")); // hash field is not 64 hex: skipped, not guessed
        Assert.False(parsed.ContainsKey("not-hex.zip"));
        Assert.Equal(2, parsed.Count);
    }

    [Fact]
    public void ABrokenFeedIsAnErrorWhileAnUninstallableReleaseIsNot()
    {
        // Malformed JSON has to stay an error. Turning it into null would let the caller say "you are on
        // the newest version" about a response it never managed to read.
        // Assert.Throws is exact-type and System.Text.Json throws the derived JsonReaderException, so this
        // has to be ThrowsAny: what matters is that a broken feed surfaces as an exception at all.
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ReleaseManifest.Parse("not json at all"));

        // Well-formed JSON naming nothing installable is not a failure, it is simply nothing to do.
        Assert.Null(ReleaseManifest.Parse("""{ "tag_name": "vNonsense" }"""));
        Assert.Null(ReleaseManifest.Parse(""));
        Assert.Null(ReleaseManifest.Resolve(null, Checksums));
    }

    [Fact]
    public void VerifyRefusesTheEmptyAndTheWrongLength()
    {
        Assert.False(ReleaseManifest.Verify(null, KnownHash));
        Assert.False(ReleaseManifest.Verify(KnownHash, null));
        Assert.False(ReleaseManifest.Verify(string.Empty, string.Empty)); // two unknowns are not a match
        Assert.False(ReleaseManifest.Verify(KnownHash, KnownHash[..63]));
    }
}
