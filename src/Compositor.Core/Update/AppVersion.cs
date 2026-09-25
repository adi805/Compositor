namespace Compositor.Core.Update;

/// <summary>
/// A release version, compared the way Sparkle compares them: numerically by component, and a version
/// carrying a prerelease label ranks BELOW the same version without one. String comparison would call
/// 0.9.0 newer than 0.10.0, so nothing about versions is allowed to go through a string.
/// </summary>
public readonly record struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    public AppVersion(int major, int minor, int patch, string prerelease = "")
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease ?? string.Empty;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>Text after the first hyphen, empty for a release. <c>0.4.0-beta.1</c> → <c>beta.1</c>.</summary>
    public string Prerelease { get; }

    /// <summary>True when this is a released version rather than a prerelease of it.</summary>
    public bool IsRelease => Prerelease.Length == 0;

    /// <summary>
    /// Parses <c>0.3.0</c>, <c>v0.3.0</c> and <c>0.4.0-beta.1</c>. A leading <c>v</c> is allowed because
    /// Git tags carry one while assembly versions do not. A missing trailing component counts as zero, so
    /// <c>1.2</c> and <c>1.2.0</c> are the same version; anything else that will not parse returns false.
    /// </summary>
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var body = text.Trim();
        if (body.Length > 0 && (body[0] == 'v' || body[0] == 'V'))
        {
            body = body[1..];
        }

        var dash = body.IndexOf('-');
        var prerelease = dash < 0 ? string.Empty : body[(dash + 1)..];
        var numbers = dash < 0 ? body : body[..dash];
        var parts = numbers.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var components = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var value) || value < 0)
            {
                return false;
            }

            components[i] = value;
        }

        version = new AppVersion(components[0], components[1], components[2], prerelease);
        return true;
    }

    /// <summary>
    /// Component order, then the rule that a prerelease is a draft of its release: equal numbers with one
    /// side labelled put the labelled side first. Two different prerelease labels fall back to text order,
    /// which is as far as the tags we actually publish go.
    /// </summary>
    public int CompareTo(AppVersion other)
    {
        var byNumber = Major.CompareTo(other.Major);
        if (byNumber != 0)
        {
            return byNumber;
        }

        byNumber = Minor.CompareTo(other.Minor);
        if (byNumber != 0)
        {
            return byNumber;
        }

        byNumber = Patch.CompareTo(other.Patch);
        if (byNumber != 0)
        {
            return byNumber;
        }

        if (IsRelease && !other.IsRelease)
        {
            return 1;
        }

        if (!IsRelease && other.IsRelease)
        {
            return -1;
        }

        return string.CompareOrdinal(Prerelease, other.Prerelease);
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is a newer release than <paramref name="installed"/>.
    /// Unparseable input on either side is answered "no": a version we cannot read is not a reason to
    /// offer an update, and a version we cannot read about the running build is not a reason to overwrite
    /// anything.
    /// </summary>
    public static bool IsNewer(string? candidate, string? installed)
    {
        if (!TryParse(candidate, out var candidateVersion) || !TryParse(installed, out var installedVersion))
        {
            return false;
        }

        return candidateVersion > installedVersion;
    }

    // CA1036 wants these alongside IComparable, and they earn their place: an update rule reads better as
    // `candidate > installed` than as a CompareTo call compared against zero.
    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    /// <summary>The three components plus a prerelease label, without the <c>v</c> a tag would carry.</summary>
    public override string ToString()
    {
        var text = $"{Major}.{Minor}.{Patch}";
        return IsRelease ? text : $"{text}-{Prerelease}";
    }
}
