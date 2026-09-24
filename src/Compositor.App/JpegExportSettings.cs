using System.Globalization;
using Compositor.Core.Imaging;

namespace Compositor.App;

/// <summary>
/// Remembers the JPEG export quality between exports, the equivalent of upstream's sheet
/// reading <c>UserDefaults.standard.double(forKey: "jpegExportQuality")</c> when it opens.
/// One value, one file - a settings framework would be more machinery than the feature.
/// </summary>
public static class JpegExportSettings
{
    /// Upstream's key name, kept so the two builds can be compared without translation.
    public const string QualityKey = "jpegExportQuality";

    /// Where the file lives when no path is given (per-user app data, created on first save).
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Compositor",
        "settings.txt");

    /// Stored quality, or the upstream default when there is no file / the value is unusable.
    public static JpegOptions Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
            {
                return new JpegOptions();
            }

            var text = File.ReadAllText(file);
            var marker = QualityKey + "=";
            var line = text
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith(marker, StringComparison.Ordinal));
            if (line is null)
            {
                return new JpegOptions();
            }

            var raw = line[marker.Length..].Trim();
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var quality)
                ? new JpegOptions { Quality = quality }.Normalize()
                : new JpegOptions();
        }
        catch (Exception)
        {
            // A missing or unreadable preference is not a reason to fail an export.
            return new JpegOptions();
        }
    }

    /// Writes the quality back. Failures are swallowed: losing a remembered slider is not an error.
    public static void Save(JpegOptions options, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var file = path ?? DefaultPath;
        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                file,
                $"{QualityKey}={options.Normalize().Quality.ToString("0.00", CultureInfo.InvariantCulture)}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Ignored on purpose; see above.
        }
    }
}
