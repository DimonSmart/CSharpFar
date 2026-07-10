using CSharpFar.App.Panels;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class ComparisonSelectionApplier
{
    public static void Apply(CompareResult result, FilePanelState left, FilePanelState right)
    {
        var leftPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rightPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (CompareResultRow row in result.Rows)
        {
            switch (row.Status)
            {
                case CompareStatus.LeftOnly:
                    AddVisibleMarkers(left, row.LeftEntries, leftPaths);
                    break;
                case CompareStatus.RightOnly:
                    AddVisibleMarkers(right, row.RightEntries, rightPaths);
                    break;
                case CompareStatus.Different:
                    AddVisibleMarkers(left, row.LeftEntries, leftPaths);
                    AddVisibleMarkers(right, row.RightEntries, rightPaths);
                    break;
                case CompareStatus.Ambiguous:
                case CompareStatus.Duplicate:
                case CompareStatus.Error:
                    AddVisibleMarkers(left, row.LeftEntries, leftPaths);
                    AddVisibleMarkers(right, row.RightEntries, rightPaths);
                    break;
            }
        }

        ReplaceSelection(left, leftPaths);
        ReplaceSelection(right, rightPaths);
    }

    private static void AddVisibleMarkers(
        FilePanelState panel,
        IReadOnlyList<FileEntry> entries,
        HashSet<string> selectedPaths)
    {
        foreach (FileEntry entry in entries)
        {
            FilePanelItem? marker = panel.Items
                .Where(item => !item.IsParentDirectory && IsSameOrAncestor(item.FullPath, entry.FullPath))
                .OrderByDescending(item => item.FullPath.Length)
                .FirstOrDefault();
            if (marker is not null)
                selectedPaths.Add(marker.FullPath);
        }
    }

    private static bool IsSameOrAncestor(string candidate, string path)
    {
        if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
            return true;

        string separatorTerminated = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(separatorTerminated, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReplaceSelection(FilePanelState panel, HashSet<string> paths)
    {
        panel.SelectedPaths.Clear();
        panel.SelectedLocations.Clear();

        foreach (FilePanelItem item in panel.Items.Where(item => !item.IsParentDirectory && paths.Contains(item.FullPath)))
        {
            panel.SelectedPaths.Add(item.FullPath);
            panel.SelectedLocations.Add(item.Location);
        }

        panel.Summary = PanelSearchResultsSummaryBuilder.BuildSummary(panel);
    }
}
