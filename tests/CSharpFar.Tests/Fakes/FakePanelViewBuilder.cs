using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.Tests.Fakes;

/// <summary>
/// Test double for IPanelViewBuilder.
/// Returns items exactly as registered in the underlying IFileSystemService,
/// with real sorting applied but without adding ".." or visibility filtering.
/// This preserves existing test semantics from before Spec 007.
/// </summary>
public sealed class FakePanelViewBuilder : IPanelViewBuilder
{
    private readonly IFileSystemService _fs;
    private readonly IPanelSortService  _sort;

    public FakePanelViewBuilder(IFileSystemService fs)
    {
        _fs   = fs;
        _sort = new PanelSortService();
    }

    public PanelView Build(PanelViewRequest request)
    {
        // Throws if the underlying service throws (used by ThrowingViewBuilder tests)
        var items = _fs.ReadDirectory(request.DirectoryPath);

        var sortOptions = new PanelSortOptions
        {
            SortFoldersByExtension   = request.Options.SortFoldersByExtension,
            KeepParentDirectoryFirst = true,
            DirectoriesFirst         = true,
        };
        var sorted = _sort.Sort(items, request.SortMode, request.SortDescending, sortOptions);

        return new PanelView
        {
            Items            = sorted,
            Summary          = new PanelSummary(),
            AutoRefreshState = new PanelAutoRefreshState(),
            IsRootDirectory  = false,
        };
    }
}
