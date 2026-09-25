using Compositor.App.Update;
using Compositor.Core.Update;
using Xunit;

namespace Compositor.App.Tests;

/// <summary>
/// The decision half of the updater, driven by an injected transport: no test here opens a socket. The
/// fixtures are a real GitHub Releases body and a real <c>sha256sum</c> line, generated per tag so a
/// release and its manifest always name each other.
/// </summary>
public sealed class UpdateCheckerTests
{
    private const string Installed = "0.3.0";
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// A release body for the given tag. One template with placeholders rather than assembled JSON: the
    /// shape has to stay readable against what the API really returns, which the Core tests pin verbatim.
    /// </summary>
    private static string ReleaseJson(string tag, bool prerelease = false, bool withSums = true)
    {
        const string Template = """
        {
          "tag_name": "TAGHERE",
          "prerelease": PREHERE,
          "html_url": "https://example.invalid/TAGHERE",
          "assets": [
            { "name": "Compositor-Windows-TAGHERE-win-x64.zip",
              "browser_download_url": "https://example.invalid/app.zip" }SUMSHERE
          ]
        }
        """;

        var sums = withSums
            ? ",\n            { \"name\": \"SHA256SUMS\", \"browser_download_url\": \"https://example.invalid/sums\" }"
            : string.Empty;
        return Template
            .Replace("PREHERE", prerelease ? "true" : "false")
            .Replace("SUMSHERE", sums)
            .Replace("TAGHERE", tag);
    }

    private static string Sums(string tag) => Hash + "  Compositor-Windows-" + tag + "-win-x64.zip\n";

    private static UpdateChecker ForRelease(string tag, bool prerelease = false, bool withSums = true)
    {
        var json = ReleaseJson(tag, prerelease, withSums);
        return new UpdateChecker(
            url => Task.FromResult(url.EndsWith("/sums", StringComparison.Ordinal) ? Sums(tag) : json),
            _ => Task.FromResult("/tmp/compositor-test-download.zip"));
    }

    [Fact]
    public async Task AFeedWithANewerTagOffersThatRelease()
    {
        var result = await ForRelease("v0.9.0").CheckAsync(Installed);

        Assert.True(result.HasRelease);
        Assert.Equal("v0.9.0", result.Release!.Value.Tag);
        Assert.Equal(Hash, result.Release.Value.Sha256);
        Assert.True(result.Release.Value.IsDownloadable);
        Assert.Contains("v0.9.0 is available", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSameTagIsNotAnUpdate()
    {
        // Release and manifest both name v0.3.0, so this can only fail on the version comparison.
        var result = await ForRelease("v0.3.0").CheckAsync(Installed);

        Assert.False(result.HasRelease);
        Assert.Null(result.Error);
        Assert.Contains("newest version", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOlderTagIsNotAnUpdate()
    {
        var result = await ForRelease("v0.2.9").CheckAsync(Installed);

        Assert.False(result.HasRelease);
    }

    [Fact]
    public async Task APrereleaseIsNeverOfferedAutomatically()
    {
        var result = await ForRelease("v0.9.0", prerelease: true).CheckAsync(Installed);

        Assert.False(result.HasRelease);
    }

    [Fact]
    public async Task AReleaseWithoutAChecksumManifestIsNotOffered()
    {
        var result = await ForRelease("v0.9.0", withSums: false).CheckAsync(Installed);

        // An unverifiable package is the same as no package: a quiet "up to date" beats an error dialog.
        Assert.False(result.HasRelease);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task AManifestThatDoesNotNameTheAssetRefusesTheRelease()
    {
        var json = ReleaseJson("v0.9.0");
        var checker = new UpdateChecker(
            url => Task.FromResult(url.EndsWith("/sums", StringComparison.Ordinal) ? Sums("v0.8.0") : json),
            _ => Task.FromResult("/tmp/x.zip"));

        var result = await checker.CheckAsync(Installed);

        Assert.False(result.HasRelease);
    }

    [Fact]
    public async Task AnUnreachableFeedReportsAFailureWithoutThrowing()
    {
        var checker = new UpdateChecker(_ => throw new HttpRequestException("no network"));

        var result = await checker.CheckAsync(Installed);

        Assert.False(result.HasRelease);
        Assert.Contains("no network", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABrokenFeedIsAFailedCheckAndNotAnUpToDate()
    {
        // What GitHub actually answers when the unauthenticated rate limit is hit: HTML, not JSON. Both
        // branches below read as "nothing happened", but only one of them is a claim about the version.
        var checker = new UpdateChecker(_ => Task.FromResult("<html>403 rate limit exceeded</html>"));

        var result = await checker.CheckAsync(Installed);

        Assert.False(result.HasRelease);
        Assert.NotNull(result.Error);
        Assert.Contains("Update check failed", result.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("newest version", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallRefusesToWriteAnythingWithoutThePhrase()
    {
        var checker = ForRelease("v0.9.0");
        var check = await checker.CheckAsync(Installed);
        var install = Path.Combine(Path.GetTempPath(), "compositor-negate-" + Guid.NewGuid().ToString("N")[..8]);

        var outcome = await checker.InstallAsync(check, install, confirmation: "ok");

        Assert.False(outcome.Succeeded);
        Assert.Contains(UpdateChecker.ConfirmationPhrase, outcome.Reason, StringComparison.Ordinal);
        Assert.Null(outcome.StagingPath);
        Assert.False(Directory.Exists(install)); // the refusal created nothing, not even the folder
    }

    [Fact]
    public async Task InstallWithoutAReleaseHasNothingToDo()
    {
        var outcome = await ForRelease("v0.9.0").InstallAsync(
            UpdateCheckResult.UpToDate(Installed), Path.GetTempPath(), UpdateChecker.ConfirmationPhrase);

        Assert.False(outcome.Succeeded);
        Assert.Contains("nothing newer", outcome.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildReportsAVersionTheComparerCanRead()
    {
        // Criterion for the whole feature: the running build has to know its own version, and know it in
        // the same format the tags arrive in. A commit suffix after '+' is not part of a version.
        var installed = UpdateChecker.InstalledVersion;

        Assert.False(string.IsNullOrWhiteSpace(installed));
        Assert.True(AppVersion.TryParse(installed, out var parsed), $"could not parse '{installed}'");
        Assert.False(installed.Contains('+'));
        Assert.Equal(0, parsed.Major);
        Assert.Equal(3, parsed.Minor);
    }
}
