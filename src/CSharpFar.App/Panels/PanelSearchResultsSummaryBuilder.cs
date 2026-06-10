using CSharpFar.Core.Models;

namespace CSharpFar.App.Panels;

internal static class PanelSearchResultsSummaryBuilder
{
    public static string BuildTitle(SearchRequest request, bool cancelled)
    {
        string basis = !string.IsNullOrEmpty(request.ContainingText)
            ? request.ContainingText
            : request.FileMaskExpression;

        string title = $"Search results: {basis}";
        return cancelled ? $"{title}, cancelled" : title;
    }

    public static PanelSummary BuildSummary(FilePanelState state)
    {
        int fileCount = 0;
        int directoryCount = 0;
        long totalFileSize = 0;
        int selectedCount = 0;
        long selectedFileSize = 0;

        foreach (var item in state.Items)
        {
            if (item.IsDirectory)
                directoryCount++;
            else
            {
                fileCount++;
                totalFileSize += item.Size ?? 0;
            }

            if (!state.SelectedPaths.Contains(item.FullPath))
                continue;

            selectedCount++;
            if (!item.IsDirectory)
                selectedFileSize += item.Size ?? 0;
        }

        return new PanelSummary
        {
            VisibleItemCount = fileCount + directoryCount,
            FileCount = fileCount,
            DirectoryCount = directoryCount,
            TotalFileSize = totalFileSize,
            SelectedCount = selectedCount,
            SelectedFileSize = selectedFileSize,
        };
    }
}
