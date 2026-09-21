using System.Globalization;

namespace CSharpFar.App.Updates;

internal readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public int CompareTo(ReleaseVersion other)
    {
        int major = Major.CompareTo(other.Major);
        if (major != 0)
            return major;

        int minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

internal static class ReleaseVersionParser
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];

        if (text.Contains('-', StringComparison.Ordinal))
            return false;

        int metadata = text.IndexOf('+');
        if (metadata >= 0)
        {
            if (metadata == 0 || metadata == text.Length - 1 || text.IndexOf('+', metadata + 1) >= 0)
                return false;
            text = text[..metadata];
        }

        string[] parts = text.Split('.');
        if (parts.Length != 3 ||
            !TryParseComponent(parts[0], out int major) ||
            !TryParseComponent(parts[1], out int minor) ||
            !TryParseComponent(parts[2], out int patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    public static bool TryParse(Version? value, out ReleaseVersion version)
    {
        version = default;
        if (value is null || value.Major < 0 || value.Minor < 0 || value.Build < 0)
            return false;

        version = new ReleaseVersion(value.Major, value.Minor, value.Build);
        return true;
    }

    private static bool TryParseComponent(string text, out int value)
    {
        value = 0;
        return text.Length > 0 &&
               text.All(static c => c is >= '0' and <= '9') &&
               int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
