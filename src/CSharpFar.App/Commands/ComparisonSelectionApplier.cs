using CSharpFar.Core.Services;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;

namespace CSharpFar.App.Commands;

internal static class ComparisonSelectionApplier
{
    public static void Apply(
        CompareResult result,
        FilePanelState left,
        FilePanelState right,
        PanelController controller)
    {
        var leftPaths = new HashSet<PanelLocation>(left.SelectedLocations.Comparer);
        var rightPaths = new HashSet<PanelLocation>(right.SelectedLocations.Comparer);

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

        ReplaceSelection(left, leftPaths, controller);
        ReplaceSelection(right, rightPaths, controller);
    }

    private static void AddVisibleMarkers(
        FilePanelState panel,
        IReadOnlyList<FileEntry> entries,
        HashSet<PanelLocation> selectedPaths)
    {
        foreach (FileEntry entry in entries)
        {
            FilePanelItem? marker = panel.Items
                .Where(item => !item.IsParentDirectory && IsSameOrAncestor(item.FullPath, entry.FullPath))
                .OrderByDescending(item => item.FullPath.Length)
                .FirstOrDefault();
            if (marker is not null)
                selectedPaths.Add(marker.Location);
        }
    }

    private static bool IsSameOrAncestor(string candidate, string path)
    {
        if (LocalFileSystemPathComparer.Current.Equals(candidate, path))
            return true;

        string separatorTerminated = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.Length >= separatorTerminated.Length &&
            LocalFileSystemPathComparer.Current.Equals(path[..separatorTerminated.Length], separatorTerminated);
    }

    private static void ReplaceSelection(
        FilePanelState panel,
        HashSet<PanelLocation> paths,
        PanelController controller)
    {
        panel.SelectedLocations.Clear();

        foreach (FilePanelItem item in panel.Items.Where(item => !item.IsParentDirectory && paths.Contains(item.Location)))
        {
            panel.SelectedLocations.Add(item.Location);
        }

        controller.RefreshSelectionSummary(panel);
    }
}
