using CSharpFar.App.Files;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class PanelFileViewerHistoryTests
{
    [Fact]
    public void BuildHistoryPath_LocalReferencedItem_UsesPlainFilesystemPath()
    {
        const string path = "/project/found.txt";
        var item = new FilePanelItem
        {
            Name = "found.txt",
            FullPath = path,
            IsDirectory = false,
        };

        Assert.Equal(path, PanelFileViewerService.BuildHistoryPath(item));
    }

    [Fact]
    public void BuildHistoryPath_NonLocalItem_KeepsSourceQualification()
    {
        var item = new FilePanelItem
        {
            Name = "remote.txt",
            FullPath = "/remote/remote.txt",
            SourceId = PanelSourceId.Demo,
            IsDirectory = false,
        };

        Assert.Equal("demo:/remote/remote.txt", PanelFileViewerService.BuildHistoryPath(item));
    }
}
