using CSharpFar.App.Commands;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class SearchResultsCapabilityTests
{
    [Fact]
    public void SearchResults_AllowEditingReferencedItemsButKeepPanelStructureReadOnly()
    {
        PanelProviderCapabilities capabilities = PanelProviderCapabilities.SearchResults;

        Assert.True(capabilities.HasFlag(PanelProviderCapabilities.Enumerate));
        Assert.True(capabilities.HasFlag(PanelProviderCapabilities.OpenRead));
        Assert.True(capabilities.HasFlag(PanelProviderCapabilities.CopyFrom));
        Assert.True(capabilities.HasFlag(PanelProviderCapabilities.Edit));
        Assert.True(capabilities.HasFlag(PanelProviderCapabilities.Refresh));

        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.OpenWrite));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.CreateFile));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.CreateDirectory));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.Delete));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.Rename));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.CopyTo));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.MoveFrom));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.MoveTo));
        Assert.False(capabilities.HasFlag(PanelProviderCapabilities.Watch));
    }

    [Fact]
    public void EditSearchResult_DoesNotRefreshSearchAfterEditorCloses()
    {
        var searchResults = new FilePanelState
        {
            SearchRequest = new SearchRequest
            {
                RootPath = "/project",
                FileMaskExpression = "*.txt",
                Scope = SearchScope.CurrentDirectoryRecursive,
                MaxDegreeOfParallelism = 1,
            },
        };
        var regularPanel = new FilePanelState();

        Assert.False(EditFileCommand.ShouldRefreshPanelAfterEdit(searchResults));
        Assert.True(EditFileCommand.ShouldRefreshPanelAfterEdit(regularPanel));
    }
}
