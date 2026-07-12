using CSharpFar.App.Commands;
using CSharpFar.Core.Comparison;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class ComparisonSelectionApplierTests
{
    [Fact]
    public void Apply_ReplacesSelectionAndMarksDirectAndNestedDifferences()
    {
        string leftRoot = P("left");
        string rightRoot = P("right");
        string leftTop = P(leftRoot, "top.txt");
        string rightTop = P(rightRoot, "top.txt");
        string leftSrc = P(leftRoot, "src");
        string rightSrc = P(rightRoot, "src");
        string leftNested = P(leftSrc, "nested.txt");

        var left = Panel(leftRoot, Item(leftTop), Item(leftSrc, directory: true));
        var right = Panel(rightRoot, Item(rightTop), Item(rightSrc, directory: true));
        left.SelectedPaths.Add(leftTop);
        right.SelectedPaths.Add(rightTop);

        ComparisonSelectionApplier.Apply(Result(
            Row(CompareStatus.Different, leftTop, rightTop),
            Row(CompareStatus.LeftOnly, leftNested, null)), left, right);

        Assert.Equal([leftSrc, leftTop], left.SelectedPaths.Order());
        Assert.Equal([rightTop], right.SelectedPaths);
        Assert.Equal(left.SelectedPaths.Count, left.SelectedLocations.Count);
        Assert.Equal(2, left.Summary!.SelectedCount);
        Assert.Equal(0, left.CursorIndex);
        Assert.Equal(0, left.ScrollOffset);
    }

    [Fact]
    public void Apply_LeavesEqualItemsAndParentDirectoryUnselected()
    {
        string leftRoot = P("left");
        string rightRoot = P("right");
        string leftSame = P(leftRoot, "same.txt");
        string rightSame = P(rightRoot, "same.txt");

        var left = Panel(leftRoot, Parent(P(leftRoot, "..")), Item(leftSame));
        var right = Panel(rightRoot, Parent(P(rightRoot, "..")), Item(rightSame));

        ComparisonSelectionApplier.Apply(Result(Row(CompareStatus.Equal, leftSame, rightSame)), left, right);

        Assert.Empty(left.SelectedPaths);
        Assert.Empty(right.SelectedPaths);
    }

    private static string P(params string[] parts) => Path.Combine(parts);

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
