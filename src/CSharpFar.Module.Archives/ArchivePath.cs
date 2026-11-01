namespace CSharpFar.Module.Archives;

internal static class ArchivePath
{
    public static string NormalizeEntry(string key, bool rarPathSeparators = false)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 2048)
            throw new InvalidDataException("Archive entry has no name.");

        // RAR commonly stores Windows-style separators even when opened on Unix.
        // Convert those only for RAR; the common validator still rejects absolute
        // names, traversal, drive prefixes and ambiguous path segments.
        if (rarPathSeparators)
            key = key.Replace('\\', '/');

        // TAR archives commonly prefix otherwise relative entries with "./".
        while (key.StartsWith("./", StringComparison.Ordinal))
            key = key[2..];

        if (key.StartsWith('/') || key.StartsWith('\\') ||
            key.Contains('\\') || key.Contains('\0') ||
            key.Any(char.IsControl))
            throw new InvalidDataException("Archive contains an unsafe absolute path or separator.");

        string[] segments = key.TrimEnd('/').Split('/');
        if (segments.Length == 0 || segments.Any(segment =>
            segment.Length == 0 || segment is "." or ".." ||
            segment.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 ||
            IsWindowsDevice(segment)))
            throw new InvalidDataException($"Unsafe archive entry path: {key}");

        return "/" + string.Join('/', segments);
    }

    public static string NormalizePanel(string path)
    {
        if (path == "/")
            return "/";
        if (!path.StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException("Archive paths must begin at the virtual root.", nameof(path));
        return NormalizeEntry(path[1..]);
    }

    public static string? Parent(string path)
    {
        path = NormalizePanel(path);
        if (path == "/")
            return null;
        int slash = path.LastIndexOf('/');
        return slash == 0 ? "/" : path[..slash];
    }

    private static bool IsWindowsDevice(string segment)
    {
        string stem = segment.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                stem[3] is >= '1' and <= '9');
    }
}
