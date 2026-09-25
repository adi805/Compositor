using Avalonia.Interactivity;
using Compositor.App.Update;

namespace Compositor.App;

/// <summary>
/// The auto-update sheet: upstream's <c>Check for Updates…</c> command, which is <c>SPUStandardUpdaterController</c>
/// on macOS. Sparkle does not exist on Windows, so this is the same user-facing affordance built from what
/// we actually have: the GitHub Releases feed, the checksum manifest the release workflow publishes, and a
/// staging folder.
/// </summary>
/// <remarks>
/// Two differences from Sparkle worth naming, both deliberate. Nothing talks to the feed on launch:
/// upstream starts its updater a second after launch because it has a signed appcast and an installer that
/// can be trusted; we have neither, so a check happens only when this menu is used. And the package is
/// staged rather than swapped in place, because a running <c>Compositor.App.exe</c> cannot overwrite
/// itself, and because deleting one folder to undo an attempt is a smaller mistake than a half-installed
/// build.
/// </remarks>
public partial class MainWindow
{
    private UpdateChecker? _updater;

    private UpdateCheckResult _updateCheck = UpdateCheckResult.UpToDate(string.Empty);

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        var checker = _updater ??= new UpdateChecker();

        UpdatePanel.IsVisible = true;
        UpdateConfirmRow.IsVisible = false;
        UpdateInstall.IsVisible = false;
        UpdatePageUrl.IsVisible = false;
        UpdateConfirmBox.Text = string.Empty;
        UpdateMessage.Text = $"Checking for updates. This build is {UpdateChecker.InstalledVersion}.";

        var result = await checker.CheckAsync(UpdateChecker.InstalledVersion);
        _updateCheck = result;
        UpdateMessage.Text = result.Summary;
        if (!result.HasRelease)
        {
            return;
        }

        // Offered, not done: the install row stays empty until the phrase is typed.
        UpdateConfirmRow.IsVisible = true;
        UpdateInstall.IsVisible = true;
        UpdatePageUrl.Text = $"Release notes: {result.Release!.Value.PageUrl}";
        UpdatePageUrl.IsVisible = true;
    }

    private async void OnUpdateInstall(object? sender, RoutedEventArgs e)
    {
        if (_updater is null)
        {
            UpdateMessage.Text = "Nothing has been checked yet, so there is nothing to install.";
            return;
        }

        UpdateMessage.Text = "Downloading the package…";

        // The directory the executable is running from, which for a self-contained publish is the whole
        // app. Passed explicitly rather than resolved by the updater so that what gets written over is
        // visible at the call site.
        var outcome = await _updater.InstallAsync(
            _updateCheck, AppContext.BaseDirectory, UpdateConfirmBox.Text);

        UpdateMessage.Text = outcome.Reason;
        if (outcome.Succeeded)
        {
            UpdateConfirmRow.IsVisible = false;
            UpdateInstall.IsVisible = false;
        }
    }

    private void OnUpdateClose(object? sender, RoutedEventArgs e) => UpdatePanel.IsVisible = false;
}
