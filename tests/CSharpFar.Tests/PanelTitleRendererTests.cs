using CSharpFar.App.Rendering;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class PanelTitleRendererTests
{
    [Fact]
    public void SourceTitle_ShowsDirectoryRelativeToInitialSourcePath()
    {
        var state = new FilePanelState
        {
            CurrentLocation = new PanelLocation(new PanelSourceId("remote"), "/home/user/docs"),
            DisplayTitle = "SFTP: server",
            DisplayTitleBasePath = "/home/user",
        };

        Assert.Equal("SFTP: server/docs", PanelTitleRenderer.FormatLabel(state, 80));
    }

    [Fact]
    public void NarrowPanel_KeepsSourceMarkerAndCurrentFolder()
    {
        var state = new FilePanelState
        {
            CurrentLocation = new PanelLocation(new PanelSourceId("archive"), "/docs/releases/reports"),
            DisplayTitle = "ZIP: very-long-archive-name.zip",
            DisplayTitleBasePath = "/",
        };

        string label = PanelTitleRenderer.FormatLabel(state, 36);

        Assert.Equal(36, label.Length);
        Assert.StartsWith("ZIP:", label);
        Assert.EndsWith("/reports", label);
        Assert.Contains('…', label);
    }

    [Fact]
    public void RootTitle_RemainsUnchanged()
    {
        var state = new FilePanelState
        {
            CurrentLocation = new PanelLocation(new PanelSourceId("archive"), "/"),
            DisplayTitle = "ZIP: backup.zip",
            DisplayTitleBasePath = "/",
        };

        Assert.Equal("ZIP: backup.zip", PanelTitleRenderer.FormatLabel(state, 80));
    }
}
