namespace CSharpFar.Shell;

/// <summary>
/// Supplies a PATH override for user-launched processes on macOS.
/// A null result means that the child should inherit PATH unchanged.
/// </summary>
public sealed class MacOsExecutablePathProvider
{
    public const string AppendStandard = "appendStandard";
    public const string Inherited = "inherited";

    private static readonly string[] StandardDirectories =
    [
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/opt/homebrew/sbin",
        "/usr/local/sbin",
    ];

    private readonly bool _appendStandard;
    private readonly Func<string?> _readPath;
    private readonly Func<string, bool> _directoryExists;
    private readonly IReadOnlyList<string> _directories;

    public MacOsExecutablePathProvider(string? mode = null)
        : this(mode, () => Environment.GetEnvironmentVariable("PATH"),
            Directory.Exists, StandardDirectories, Console.Error.WriteLine)
    {
    }

    internal MacOsExecutablePathProvider(
        string? mode,
        Func<string?> readPath,
        Func<string, bool> directoryExists,
        IReadOnlyList<string> directories,
        Action<string>? reportWarning = null)
    {
        _readPath = readPath;
        _directoryExists = directoryExists;
        _directories = directories;
        _appendStandard = mode is null or "" or AppendStandard;
        if (!_appendStandard && mode != Inherited)
            reportWarning?.Invoke($"Unknown shell.macOsPathMode '{mode}'; using inherited PATH.");
    }

    public string? GetPath()
    {
        if (!_appendStandard)
            return null;

        string path = _readPath() is { Length: > 0 } inheritedPath
            ? inheritedPath : "/usr/bin:/bin:/usr/sbin:/sbin";

        // Compare absolute paths without trailing slashes, but keep the original
        // PATH string completely intact (including duplicates and empty entries).
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (string segment in path.Split(':'))
        {
            string? normalized = NormalizeAbsolute(segment);
            if (normalized is not null)
                known.Add(normalized);
        }

        foreach (string candidate in _directories)
        {
            string? normalized = NormalizeAbsolute(candidate);
            if (normalized is null || known.Contains(normalized))
                continue;

            bool exists;
            try { exists = _directoryExists(candidate); }
            catch { exists = false; }

            if (!exists)
                continue;

            path += ":" + candidate;
            known.Add(normalized);
        }

        return path;
    }

    private static string? NormalizeAbsolute(string value)
    {
        if (!value.StartsWith('/', StringComparison.Ordinal))
            return null;
        string result = value.TrimEnd('/');
        return result.Length == 0 ? "/" : result;
    }
}
