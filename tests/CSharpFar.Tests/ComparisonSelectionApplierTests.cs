using CSharpFar.App.Commands;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class ComparisonSelectionApplierTests
{
    [Fact]
    public void Apply_ReplacesSelectionAndMarksDirectAndNestedDifferences()
    {
        var left = Panel("C:\\left", Item("C:\\left\\top.txt"), Item("C:\\left\\src", directory: true));
        var right = Panel("C:\\right", Item("C:\\right\\top.txt"), Item("C:\\right\\src", directory: true));
        left.SelectedPaths.Add("C:\\left\\top.txt");
        right.SelectedPaths.Add("C:\\right\\top.txt");

        ComparisonSelectionApplier.Apply(Result(
            Row(CompareStatus.Different, "C:\\left\\top.txt", "C:\\right\\top.txt"),
            Row(CompareStatus.LeftOnly, "C:\\left\\src\\nested.txt", null)), left, right);

        Assert.Equal(["C:\\left\\src", "C:\\left\\top.txt"], left.SelectedPaths.Order());
        Assert.Equal(["C:\\right\\top.txt"], right.SelectedPaths);
        Assert.Equal(left.SelectedPaths.Count, left.SelectedLocations.Count);
        Assert.Equal(2, left.Summary!.SelectedCount);
        Assert.Equal(0, left.CursorIndex);
        Assert.Equal(0, left.ScrollOffset);
    }

    [Fact]
    public void Apply_LeavesEqualItemsAndParentDirectoryUnselected()
    {
        var left = Panel("C:\\left", Parent("C:\\left\\.."), Item("C:\\left\\same.txt"));
        var right = Panel("C:\\right", Parent("C:\\right\\.."), Item("C:\\right\\same.txt"));

        ComparisonSelectionApplier.Apply(Result(Row(CompareStatus.Equal, "C:\\left\\same.txt", "C:\\right\\same.txt")), left, right);

        Assert.Empty(left.SelectedPaths);
        Assert.Empty(right.SelectedPaths);
    }

    private static FilePanelState Panel(string path, params FilePanelItem[] items)
    {
        var panel = new FilePanelState { CurrentDirectory = path };
        panel.Items.AddRange(items);
        return panel;
    }

    private static FilePanelItem Item(string path, bool directory = false) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        IsDirectory = directory,
        Size = directory ? null : 10,
    };

    private static FilePanelItem Parent(string path) => new()
    {
        Name = "..",
        FullPath = path,
        IsDirectory = true,
        IsParentDirectory = true,
    };

    private static CompareResult Result(params CompareResultRow[] rows) => new()
    {
        Mode = CompareMode.FolderStructure,
        Rows = rows,
        Summary = new CompareSummary(),
    };

    private static CompareResultRow Row(CompareStatus status, string? left, string? right) => new()
    {
        Status = status,
        Key = "test",
        LeftEntries = left is null ? [] : [Entry(left)],
        RightEntries = right is null ? [] : [Entry(right)],
    };

    private static FileEntry Entry(string path) => new()
    {
        FullPath = path,
        RelativePath = Path.GetFileName(path),
        FileName = Path.GetFileName(path),
        Size = 10,
    };
}
