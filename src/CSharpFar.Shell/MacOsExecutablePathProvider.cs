namespace CSharpFar.Shell;

/// <summary>Non-sensitive details about the effective PATH decision (never the PATH itself).</summary>
public sealed record MacOsPathDiagnostics(
    string Mode, string Source, string Shell, bool? ShellSucceeded,
    string? FallbackReason, long ElapsedMilliseconds);

/// <summary>
/// Supplies one consistent PATH override for all user-launched child processes on macOS.
/// A null result means the child inherits PATH unchanged.
/// </summary>
public sealed class MacOsExecutablePathProvider
{
    public const string Auto = "auto";
    public const string AppendStandard = "appendStandard";
    public const string Inherited = "inherited";
    public const string FinderLaunchEnvironmentVariable = "CSHARPFAR_FINDER_LAUNCH";

    private static readonly string[] StandardDirectories =
    [
        "/opt/homebrew/bin", "/usr/local/bin", "/opt/homebrew/sbin", "/usr/local/sbin",
    ];

    private static MacOsPathDiagnostics? s_currentDiagnostics;

    /// <summary>Snapshot of the running macOS host; reading never starts a shell.</summary>
    public static MacOsPathDiagnostics? CurrentDiagnostics =>
        System.Threading.Volatile.Read(ref s_currentDiagnostics);

    private readonly string _mode;
    private readonly Func<string?> _readPath;
    private readonly Func<string, bool> _directoryExists;
    private readonly IReadOnlyList<string> _directories;
    private readonly Func<MacOsUserShellPathResult> _resolveUserPath;
    private readonly Func<bool> _launchedFromFinder;
    private readonly bool _publishDiagnostics;
    private readonly Lazy<(string? Path, MacOsPathDiagnostics Details)> _cached;

    public MacOsExecutablePathProvider(string? mode = null)
        : this(mode, () => Environment.GetEnvironmentVariable("PATH"),
            Directory.Exists, StandardDirectories, Console.Error.WriteLine,
            new MacOsUserShellPathResolver().Resolve,
            () => Environment.GetEnvironmentVariable(FinderLaunchEnvironmentVariable) == "1",
            publishDiagnostics: true)
    {
    }

    internal MacOsExecutablePathProvider(
        string? mode,
        Func<string?> readPath,
        Func<string, bool> directoryExists,
        IReadOnlyList<string> directories,
        Action<string>? reportWarning = null,
        Func<MacOsUserShellPathResult>? resolveUserPath = null,
        Func<bool>? launchedFromFinder = null,
        bool publishDiagnostics = false)
    {
        _readPath = readPath;
        _directoryExists = directoryExists;
        _directories = directories;
        _resolveUserPath = resolveUserPath ?? new MacOsUserShellPathResolver().Resolve;
        _launchedFromFinder = launchedFromFinder ?? (() => false);
        _publishDiagnostics = publishDiagnostics;
        _mode = mode is null or "" ? Auto : mode;
        if (_mode is not (Auto or AppendStandard or Inherited))
        {
            reportWarning?.Invoke($"Unknown shell.macOsPathMode '{mode}'; using appendStandard.");
            _mode = AppendStandard;
        }

        if (_publishDiagnostics)
            System.Threading.Volatile.Write(ref s_currentDiagnostics,
                new MacOsPathDiagnostics(_mode, "not initialized", "none", null, null, 0));

        _cached = new Lazy<(string?, MacOsPathDiagnostics)>(
            Compute, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string? GetPath() => _cached.Value.Path;

    public MacOsPathDiagnostics GetDiagnostics() =>
        _cached.IsValueCreated
            ? _cached.Value.Details
            : new(_mode, "not initialized", "none", null, null, 0);

    private (string? Path, MacOsPathDiagnostics Details) Compute()
    {
        string? inherited = _readPath();
        string? effective;
        MacOsPathDiagnostics diagnostics;
        if (_mode == Inherited)
        {
            effective = null;
            diagnostics = new(_mode, "inherited", "none", null, null, 0);
        }
        else if (_mode == Auto && _launchedFromFinder())
        {
            MacOsUserShellPathResult result;
            try { result = _resolveUserPath(); }
            catch (Exception ex)
            {
                result = new(null, "unknown", false,
                    "resolver error (" + ex.GetType().Name + ")", 0);
            }

            if (result.Success && !string.IsNullOrWhiteSpace(result.Path))
            {
                effective = MergeAuto(result.Path, inherited);
                diagnostics = new(_mode, "user shell", result.Shell, true,
                    null, result.ElapsedMilliseconds);
            }
            else
            {
                effective = AppendStandardDirectories(inherited);
                diagnostics = new(_mode, "fallback", result.Shell, false,
                    result.FailureReason ?? "invalid shell PATH", result.ElapsedMilliseconds);
            }
        }
        else
        {
            effective = AppendStandardDirectories(inherited);
            diagnostics = new(_mode, "inherited", "none", null, null, 0);
        }

        if (_publishDiagnostics)
            System.Threading.Volatile.Write(ref s_currentDiagnostics, diagnostics);
        return (effective, diagnostics);
    }

    private string MergeAuto(string userPath, string? inherited)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var output = new List<string>();

        void Add(string? paths)
        {
            if (paths is null)
                return;
            foreach (string segment in paths.Split(':'))
            {
                if (segment.Length == 0)
                    continue;
                string key = NormalizeSegment(segment);
                if (seen.Add(key))
                    output.Add(segment);
            }
        }

        Add(userPath);
        Add(inherited);
        if (output.Count == 0)
            Add("/usr/bin:/bin:/usr/sbin:/sbin");

        foreach (string directory in _directories)
        {
            if (directory.Length == 0 || seen.Contains(NormalizeSegment(directory)) ||
                !SafeDirectoryExists(directory))
                continue;
            seen.Add(NormalizeSegment(directory));
            output.Add(directory);
        }

        return string.Join(":", output);
    }

    // Keep the legacy appendStandard semantics, including original duplicate/empty entries.
    private string AppendStandardDirectories(string? inherited)
    {
        string path = !string.IsNullOrEmpty(inherited)
            ? inherited : "/usr/bin:/bin:/usr/sbin:/sbin";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string segment in path.Split(':'))
        {
            if (segment.StartsWith("/", StringComparison.Ordinal))
                seen.Add(NormalizeSegment(segment));
        }

        foreach (string directory in _directories)
        {
            if (!directory.StartsWith("/", StringComparison.Ordinal) ||
                seen.Contains(NormalizeSegment(directory)) || !SafeDirectoryExists(directory))
                continue;
            path += ":" + directory;
            seen.Add(NormalizeSegment(directory));
        }
        return path;
    }

    private bool SafeDirectoryExists(string path)
    {
        try { return _directoryExists(path); }
        catch { return false; }
    }

    private static string NormalizeSegment(string value)
    {
        string trimmed = value.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }
}
