namespace CSharpFar.Shell;

public sealed class UnixEnvironment : IUnixEnvironment
{
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string> _readProcVersion;

    public UnixEnvironment()
        : this(
            Environment.GetEnvironmentVariable,
            static () => File.Exists("/proc/version") ? File.ReadAllText("/proc/version") : string.Empty)
    {
    }

    internal UnixEnvironment(Func<string, string?> getEnvironmentVariable, Func<string> readProcVersion)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _readProcVersion = readProcVersion;
    }

    public bool IsWsl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_getEnvironmentVariable("WSL_DISTRO_NAME")))
                return true;

            string version;
            try { version = _readProcVersion(); }
            catch { return false; }

            return version.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                   version.Contains("WSL", StringComparison.OrdinalIgnoreCase);
        }
    }
}
