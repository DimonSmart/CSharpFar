using CSharpFar.Core.Models;

namespace CSharpFar.Core.Abstractions;

public interface IPanelSortService
{
    IReadOnlyList<FilePanelItem> Sort(
        IReadOnlyList<FilePanelItem> items,
        SortMode sortMode,
        bool descending,
        PanelSortOptions options);

    SortDebugInfo ExplainSortKey(
        FilePanelItem item,
        SortMode sortMode,
        PanelSortOptions options);
}
