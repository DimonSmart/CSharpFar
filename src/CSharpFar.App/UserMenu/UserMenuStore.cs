using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

/// <summary>Loaded global user menu, with first-run defaults and save-on-success snapshots.</summary>
public sealed class UserMenuStore
{
    private readonly string _filePath;

    internal string FilePath => _filePath;

    public IReadOnlyList<UserMenuItem> Items { get; private set; }

    internal PlatformKind? RuntimePlatform { get; set; }

    public UserMenuStore(string configDirectory)
    {
        _filePath = Path.Combine(configDirectory, "user-menu.json");
        Items = Load();
    }

    public void Save(IReadOnlyList<UserMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        UserMenuItem[] snapshot = UserMenuItemRules.NormalizeItems(items);
        UserMenuFile.WriteSafely(_filePath, snapshot);
        Items = snapshot;
    }

    private IReadOnlyList<UserMenuItem> Load()
    {
        UserMenuItem[]? persisted = UserMenuFile.ReadOrNull(_filePath);
        if (persisted is not null)
            return persisted;

        List<UserMenuItem> defaults = CreateDefaults();
        try
        {
            UserMenuFile.WriteSafely(_filePath, defaults);
        }
        catch (Exception)
        {
            // First-run defaults remain available in memory if best-effort persistence fails.
        }
        return defaults;
    }

    private static List<UserMenuItem> CreateDefaults() =>
    [
        new UserMenuItem
        {
            Title = "Open Explorer here",
            Command = "explorer \"{panelDir}\"",
            Platform = PlatformKind.Windows,
        },
        new UserMenuItem
        {
            Title = "Open Finder here",
            Command = "open \"{panelDir}\"",
            Platform = PlatformKind.MacOs,
        },
        new UserMenuItem
        {
            Title = "Open in file manager here",
            Command = "xdg-open \"{panelDir}\"",
            Platform = PlatformKind.Linux,
        },
    ];
}
