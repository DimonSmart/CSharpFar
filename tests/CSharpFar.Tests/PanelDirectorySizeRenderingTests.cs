using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class PanelDirectorySizeRenderingTests
{
    [Theory]
    [InlineData(null, false, "   <DIR>")]
    [InlineData(null, true, "       …")]
    [InlineData(12_000_000L, true, "    12M…")]
    [InlineData(12_000_000L, false, "     12M")]
    [InlineData(15_000_000L, false, "     15M")]
    public void FullPanelRow_UsesExistingFileFormatterAndProgressMarker(
        long? displaySize,
        bool inProgress,
        string expected)
    {
        var item = DirectoryItem();
        PanelDirectorySizePresentation? presentation = displaySize is null && !inProgress
            ? null
            : new PanelDirectorySizePresentation(displaySize, inProgress);

        Assert.Equal(expected, PanelRenderer.FormatSizePart(item, 8, presentation));
    }

    [Fact]
    public void FullPanelRow_RefreshKeepsPreviousCompletedValue()
    {
        var item = DirectoryItem();

        string completed = PanelRenderer.FormatSizePart(item, 8, new PanelDirectorySizePresentation(12_000_000, false));
        string refreshing = PanelRenderer.FormatSizePart(item, 8, new PanelDirectorySizePresentation(12_000_000, true));
        string refreshed = PanelRenderer.FormatSizePart(item, 8, new PanelDirectorySizePresentation(15_000_000, false));

        Assert.Equal("     12M", completed);
        Assert.Equal("    12M…", refreshing);
        Assert.Equal("     15M", refreshed);
    }

    [Fact]
    public void StatusLine_UsesStatusFormatterForInitialProgressRefreshAndCompletion()
    {
        var item = DirectoryItem();
        var state = new FilePanelState { CursorIndex = 0 };
        state.Items.Add(item);

        string initial = PanelStatusRenderer.FormatCurrentItem(
            state,
            50,
            _ => new PanelDirectorySizePresentation(null, true));
        string partial = PanelStatusRenderer.FormatCurrentItem(
            state,
            50,
            _ => new PanelDirectorySizePresentation(12_000_000, true));
        string completed = PanelStatusRenderer.FormatCurrentItem(
            state,
            50,
            _ => new PanelDirectorySizePresentation(12_000_000, false));
        string refresh = PanelStatusRenderer.FormatCurrentItem(
            state,
            50,
            _ => new PanelDirectorySizePresentation(12_000_000, true));
        string refreshed = PanelStatusRenderer.FormatCurrentItem(
            state,
            50,
            _ => new PanelDirectorySizePresentation(15_000_000, false));

        Assert.Contains("…", initial);
        Assert.Contains("12 M…", partial);
        Assert.Contains("12 M", completed);
        Assert.DoesNotContain("12 M…", completed);
        Assert.Contains("12 M…", refresh);
        Assert.Contains("15 M", refreshed);
    }

    [Fact]
    public void FilePresentationIsUnchanged()
    {
        var file = new FilePanelItem
        {
            Name = "file.bin",
            FullPath = "/file.bin",
            IsDirectory = false,
            Size = 12_000_000,
        };

        Assert.Equal("     12M", PanelRenderer.FormatSizePart(
            file,
            8,
            new PanelDirectorySizePresentation(999_000_000, true)));
    }

    [Fact]
    public void ParentDirectoryNeverDisplaysCalculatedSize()
    {
        var parent = new FilePanelItem
        {
            Name = "..",
            FullPath = "/",
            IsDirectory = true,
            IsParentDirectory = true,
        };

        Assert.Equal(
            "        ",
            PanelRenderer.FormatSizePart(parent, 8, new PanelDirectorySizePresentation(123, true)));
    }

    private static FilePanelItem DirectoryItem() => new()
    {
        Name = "Folder",
        FullPath = "/Folder",
        IsDirectory = true,
        Size = null,
        Attributes = FileAttributes.Directory,
    };
}
