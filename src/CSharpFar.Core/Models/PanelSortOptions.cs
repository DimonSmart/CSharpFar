namespace CSharpFar.Core.Models;

public sealed class PanelSortOptions
{
    public bool SortFoldersByExtension { get; init; } = true;
    public bool KeepParentDirectoryFirst { get; init; } = true;
    public bool DirectoriesFirst { get; init; } = true;
    public StringComparer NameComparer { get; init; } = StringComparer.OrdinalIgnoreCase;
}
