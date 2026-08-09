using System.Globalization;
using System.Text.RegularExpressions;

namespace RaysLoader.Services;

public readonly partial record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public string InstallerFileName => $"RaysLoader-Setup-{this}.exe";

    public static ReleaseVersion ParseTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) throw new InvalidDataException("The release tag is empty.");
        var match = StableTagPattern().Match(tag.Trim());
        if (!match.Success) throw new InvalidDataException($"The release tag '{tag}' must be a stable major.minor.patch version.");

        return new ReleaseVersion(
            ParseComponent(match.Groups[1].Value, tag),
            ParseComponent(match.Groups[2].Value, tag),
            ParseComponent(match.Groups[3].Value, tag));
    }

    public static ReleaseVersion FromAssemblyVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Major < 0 || version.Minor < 0 || version.Build < 0 || version.Revision > 0)
        {
            throw new InvalidDataException($"Assembly version '{version}' is not a stable major.minor.patch version.");
        }
        return new ReleaseVersion(version.Major, version.Minor, version.Build);
    }

    public int CompareTo(ReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    private static int ParseComponent(string value, string tag)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var component))
            throw new InvalidDataException($"The release tag '{tag}' contains an unsupported version component.");
        return component;
    }

    [GeneratedRegex("^v?(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex StableTagPattern();
}
