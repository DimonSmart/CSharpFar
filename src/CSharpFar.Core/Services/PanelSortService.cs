using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

public sealed class PanelSortService : IPanelSortService
{
    public IReadOnlyList<FilePanelItem> Sort(
        IReadOnlyList<FilePanelItem> items,
        SortMode sortMode,
        bool descending,
        PanelSortOptions options)
    {
        var parent = options.KeepParentDirectoryFirst
            ? items.FirstOrDefault(i => i.IsParentDirectory)
            : null;

        var rest = items.Where(i => !i.IsParentDirectory).ToList();

        IEnumerable<FilePanelItem> sorted;

        if (options.DirectoriesFirst)
        {
            var dirs = rest.Where(i => i.IsDirectory).ToList();
            var files = rest.Where(i => !i.IsDirectory).ToList();

            sorted = SortGroup(dirs, sortMode, descending, options, isDirectoryGroup: true)
                .Concat(SortGroup(files, sortMode, descending, options, isDirectoryGroup: false));
        }
        else
        {
            sorted = SortGroup(rest, sortMode, descending, options, isDirectoryGroup: null);
        }

        var result = new List<FilePanelItem>();
        if (parent is not null) result.Add(parent);
        result.AddRange(sorted);
        return result;
    }

    public SortDebugInfo ExplainSortKey(
        FilePanelItem item,
        SortMode sortMode,
        PanelSortOptions options)
    {
        string primary = GetPrimaryKey(item, sortMode, options);
        string secondary = item.Name;
        return new SortDebugInfo
        {
            PrimaryKey = primary,
            SecondaryKey = secondary,
            IsDirectory = item.IsDirectory,
            IsParentDirectory = item.IsParentDirectory,
        };
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static IEnumerable<FilePanelItem> SortGroup(
        List<FilePanelItem> group,
        SortMode sortMode,
        bool descending,
        PanelSortOptions options,
        bool? isDirectoryGroup)
    {
        // For extension sort: if this is a directory group and SortFoldersByExtension=false,
        // sort dirs by name instead of extension.
        bool useExtForDirs = sortMode == SortMode.Extension
            && (isDirectoryGroup != true || options.SortFoldersByExtension);

        // Primary key is ordered by descending when requested;
        // secondary key (name) always stays ascending so equal groups don't jump.
        return sortMode switch
        {
            SortMode.Name when descending =>
                group.OrderByDescending(i => i.Name, options.NameComparer),

            SortMode.Name =>
                group.OrderBy(i => i.Name, options.NameComparer),

            SortMode.Extension when useExtForDirs && descending =>
                group.OrderByDescending(i => Path.GetExtension(i.Name), options.NameComparer)
                     .ThenBy(i => i.Name, options.NameComparer),

            SortMode.Extension when useExtForDirs =>
                group.OrderBy(i => Path.GetExtension(i.Name), options.NameComparer)
                     .ThenBy(i => i.Name, options.NameComparer),

            SortMode.Extension when descending =>
                group.OrderByDescending(i => i.Name, options.NameComparer),

            SortMode.Extension =>
                group.OrderBy(i => i.Name, options.NameComparer),

            SortMode.Size when descending =>
                group.OrderByDescending(i => i.Size ?? 0)
                     .ThenBy(i => i.Name, options.NameComparer),

            SortMode.Size =>
                group.OrderBy(i => i.Size ?? 0)
                     .ThenBy(i => i.Name, options.NameComparer),

            SortMode.LastWriteTime when descending =>
                group.OrderByDescending(i => i.LastWriteTime)
                     .ThenBy(i => i.Name, options.NameComparer),

            SortMode.LastWriteTime =>
                group.OrderBy(i => i.LastWriteTime)
                     .ThenBy(i => i.Name, options.NameComparer),

            _ when descending =>
                group.OrderByDescending(i => i.Name, options.NameComparer),

            _ =>
                group.OrderBy(i => i.Name, options.NameComparer),
        };
    }

    private static string GetPrimaryKey(FilePanelItem item, SortMode sortMode, PanelSortOptions options)
    {
        if (item.IsParentDirectory) return string.Empty;
        return sortMode switch
        {
            SortMode.Name => item.Name,
            SortMode.Extension => item.IsDirectory && !options.SortFoldersByExtension
                                      ? item.Name
                                      : Path.GetExtension(item.Name),
            SortMode.Size => (item.Size ?? 0).ToString(),
            SortMode.LastWriteTime => item.LastWriteTime.ToString("O"),
            _ => item.Name,
        };
    }
}
