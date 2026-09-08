using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.Tests;

public sealed class PanelDirectorySizeSortingTests
{
    [Fact]
    public void SortBySize_ContinuesToUseFilePanelItemSizeOnly()
    {
        var first = Directory("z-folder");
        var second = Directory("a-folder");
        var service = new PanelSortService();

        IReadOnlyList<FilePanelItem> result = service.Sort(
            [first, second],
            SortMode.Size,
            descending: false,
            new PanelSortOptions
            {
                DirectoriesFirst = true,
                KeepParentDirectoryFirst = true,
            });

        Assert.Equal(["a-folder", "z-folder"], result.Select(item => item.Name));
        Assert.Null(first.Size);
        Assert.Null(second.Size);
    }

    private static FilePanelItem Directory(string name) => new()
    {
        Name = name,
        FullPath = "/" + name,
        IsDirectory = true,
        Size = null,
        Attributes = FileAttributes.Directory,
    };
}
