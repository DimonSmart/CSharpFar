using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class ShellRegistry
{
    private readonly Dictionary<string, ShellProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public ShellRegistry(IShellCommandLineBuilder powerShellBuilder)
    {
        Register(new ShellProfile("powershell", ["ps", "pwsh", "powershell"], powerShellBuilder));
    }

    public bool TryGet(string alias, out ShellProfile profile) => _profiles.TryGetValue(alias, out profile!);

    private void Register(ShellProfile profile)
    {
        foreach (string alias in profile.Aliases)
            _profiles.Add(alias, profile);
    }
}

public sealed record ShellProfile(string Id, IReadOnlyList<string> Aliases, IShellCommandLineBuilder CommandLineBuilder);
