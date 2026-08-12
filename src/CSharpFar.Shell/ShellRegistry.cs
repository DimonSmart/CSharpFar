using CSharpFar.Core.Abstractions;

namespace CSharpFar.Shell;

public sealed class ShellRegistry
{
    private readonly Dictionary<string, ShellProfile> _profilesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ShellProfile> _profilesByAlias = new(StringComparer.OrdinalIgnoreCase);

    public ShellRegistry(params ShellProfile[] profiles)
    {
        foreach (ShellProfile profile in profiles)
            Add(profile);
    }

    public bool TryResolveAlias(string alias, out ShellProfile profile) =>
        _profilesByAlias.TryGetValue(alias, out profile!);

    public bool TryGetById(string id, out ShellProfile profile) =>
        _profilesById.TryGetValue(id, out profile!);

    private void Add(ShellProfile profile)
    {
        _profilesById.Add(profile.Id, profile);
        foreach (string alias in profile.Aliases)
            _profilesByAlias.Add(alias, profile);
    }
}

public sealed record ShellProfile(string Id, IReadOnlyList<string> Aliases, IShellCommandLineBuilder CommandLineBuilder);
