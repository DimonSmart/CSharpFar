using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Sorting: SortFoldersByExtension, parent stays first, SortDebugInfo.
/// </summary>
public sealed class Spec007SortingTests
{
    private static readonly PanelSortService Sort = new();

    private static FilePanelItem File(string name, long size = 0) =>
        new() { Name = name, FullPath = @"C:\" + name, IsDirectory = false, Size = size };

    private static FilePanelItem Dir(string name) =>
        new() { Name = name, FullPath = @"C:\" + name, IsDirectory = true };

    private static FilePanelItem Parent() =>
        new() { Name = "..", FullPath = @"C:\", IsDirectory = true, IsParentDirectory = true };

    // ── SortFoldersByExtension ────────────────────────────────────────────────

    [Fact]
    public void ExtensionSort_Folders_SortedByExtension_WhenEnabled()
    {
        var items = new[] { Dir("src.git"), Dir("docs"), Dir("backup.zip") };
        var opts = new PanelSortOptions { SortFoldersByExtension = true, DirectoriesFirst = true };

        var result = Sort.Sort(items, SortMode.Extension, false, opts);

        // No ext: docs  < .git: src.git  < .zip: backup.zip
        Assert.Equal("docs", result[0].Name);
        Assert.Equal("src.git", result[1].Name);
        Assert.Equal("backup.zip", result[2].Name);
    }

    [Fact]
    public void ExtensionSort_Folders_SortedByName_WhenDisabled()
    {
        var items = new[] { Dir("src.git"), Dir("docs"), Dir("backup.zip") };
        var opts = new PanelSortOptions { SortFoldersByExtension = false, DirectoriesFirst = true };

        var result = Sort.Sort(items, SortMode.Extension, false, opts);

        // Dirs sorted by name alphabetically
        Assert.Equal("backup.zip", result[0].Name);
        Assert.Equal("docs", result[1].Name);
        Assert.Equal("src.git", result[2].Name);
    }

    [Fact]
    public void ExtensionSort_Files_AlwaysByExtension()
    {
        var items = new[] { File("readme.md"), File("image.png"), File("notes.md") };
        var opts = new PanelSortOptions { SortFoldersByExtension = false, DirectoriesFirst = false };

        var result = Sort.Sort(items, SortMode.Extension, false, opts);

        // .md < .png; within .md: notes < readme
        Assert.Equal("notes.md", result[0].Name);
        Assert.Equal("readme.md", result[1].Name);
        Assert.Equal("image.png", result[2].Name);
    }

    // ── Parent directory stays first ──────────────────────────────────────────

    [Fact]
    public void Sort_ParentDirectory_StaysFirst()
    {
        var items = new FilePanelItem[] { Parent(), Dir("Zebra"), Dir("Apple"), File("a.txt") };
        var opts = new PanelSortOptions { KeepParentDirectoryFirst = true, DirectoriesFirst = true };

        var result = Sort.Sort(items, SortMode.Name, descending: true, opts);

        Assert.True(result[0].IsParentDirectory, ".. must be first even when descending");
    }

    // ── Descending secondary key stays stable ─────────────────────────────────

    [Fact]
    public void DescendingSort_EqualPrimaryKey_SecondaryAscending()
    {
        // Two files with the same size; secondary (name) should be ascending
        var items = new[] { File("beta.txt", 100), File("alpha.txt", 100) };
        var opts = new PanelSortOptions { DirectoriesFirst = false };

        var result = Sort.Sort(items, SortMode.Size, descending: true, opts);

        Assert.Equal("alpha.txt", result[0].Name);
        Assert.Equal("beta.txt", result[1].Name);
    }

    // ── SortDebugInfo ─────────────────────────────────────────────────────────

    [Fact]
    public void ExplainSortKey_NameMode_PrimaryKeyIsName()
    {
        var item = File("hello.txt");
        var opts = new PanelSortOptions();

        var info = Sort.ExplainSortKey(item, SortMode.Name, opts);

        Assert.Equal("hello.txt", info.PrimaryKey);
        Assert.Equal("hello.txt", info.SecondaryKey);
        Assert.False(info.IsDirectory);
        Assert.False(info.IsParentDirectory);
    }

    [Fact]
    public void ExplainSortKey_ExtensionMode_PrimaryKeyIsExtension()
    {
        var item = File("file.csv");
        var opts = new PanelSortOptions { SortFoldersByExtension = true };

        var info = Sort.ExplainSortKey(item, SortMode.Extension, opts);

        Assert.Equal(".csv", info.PrimaryKey);
    }

    [Fact]
    public void ExplainSortKey_ExtensionMode_DirWithDisabled_PrimaryKeyIsName()
    {
        var item = Dir("src.git");
        var opts = new PanelSortOptions { SortFoldersByExtension = false };

        var info = Sort.ExplainSortKey(item, SortMode.Extension, opts);

        Assert.Equal("src.git", info.PrimaryKey);
    }

    [Fact]
    public void ExplainSortKey_ParentDirectory_PrimaryKeyIsEmpty()
    {
        var item = Parent();
        var opts = new PanelSortOptions();

        var info = Sort.ExplainSortKey(item, SortMode.Name, opts);

        Assert.Equal(string.Empty, info.PrimaryKey);
        Assert.True(info.IsParentDirectory);
    }
}
