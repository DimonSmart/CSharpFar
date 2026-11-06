using CSharpFar.Core.Models;

namespace CSharpFar.App.UserMenu;

internal sealed record ResolvedUserMenuItem(UserMenuItem Item, string SourcePath, string MenuDirectory);

internal sealed record ResolvedUserMenuSection(
    string Label,
    string SourcePath,
    IReadOnlyList<ResolvedUserMenuItem> Items);

internal sealed record ResolvedUserMenu(
    IReadOnlyList<ResolvedUserMenuSection> Sections,
    bool HasLocalMenus);

internal static class UserMenuResolver
{
    public const string LocalFileName = ".csharpfar-menu.json";

    public static ResolvedUserMenu Resolve(UserMenuStore global, PlatformKind platform, string? localDirectory)
    {
        var sources = new List<(string Label, string Path, UserMenuItem[] Items)>();
        bool hasLocal = false;
        if (localDirectory is not null)
        {
            string directory = Path.GetFullPath(localDirectory);
            while (true)
            {
                string path = Path.Combine(directory, LocalFileName);
                UserMenuItem[]? local = UserMenuFile.ReadOrNull(path);
                if (local is not null)
                {
                    hasLocal = true;
                    string label = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
                    sources.Add((string.IsNullOrEmpty(label) ? directory : label, path, local));
                }

                string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory));
                if (parent is null || parent == directory)
                    break;
                directory = parent;
            }
        }

        // Validation must finish for every source before any partial menu is displayed.
        string? globalError = UserMenuItemRules.FindValidationError(global.Items);
        if (globalError is not null)
            throw new InvalidDataException("Invalid user menu '" + global.FilePath + "': " + globalError);

        sources.Add(("Global", global.FilePath, UserMenuItemRules.CloneItems(global.Items)));
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var sections = new List<ResolvedUserMenuSection>();
        foreach (var source in sources)
        {
            var visible = new List<ResolvedUserMenuItem>();
            foreach (UserMenuItem item in source.Items)
            {
                if (!UserMenuAvailability.IsAvailableOn(item, platform))
                    continue;
                if (item.Id is { } id && !seenIds.Add(id))
                    continue;
                visible.Add(new ResolvedUserMenuItem(item, source.Path, Path.GetDirectoryName(source.Path)!));
            }

            if (visible.Count > 0)
                sections.Add(new ResolvedUserMenuSection(source.Label, source.Path, visible));
        }
        return new ResolvedUserMenu(sections, hasLocal);
    }
}
