using CSharpFar.App.Panels;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class NestedPanelNavigationTests
{
    [Fact]
    public void LeavingNestedRoot_RestoresOriginItemAndReleasesNavigationLease()
    {
        var outer = new InMemoryBatchFilePanelSource();
        var inner = new InMemoryBatchFilePanelSource();
        outer.SeedFile("/container.bin", "container");
        inner.SeedFile("/internal.txt", "contents");

        var registry = new FilePanelSourceRegistry([]);
        Assert.True(registry.TryAddOwned(outer));
        Assert.True(registry.TryAddOwned(inner));
        var controller = new PanelController(
            new PanelViewBuilder(new FakeFileSystemService(), new PanelSortService(), sources: registry));
        var options = new AppSettings.PanelOptionsSettings();
        var state = new FilePanelState();
        controller.LoadLocation(state, new PanelLocation(outer.SourceId, "/"), options);
        var navigation = new PanelNavigationService(controller, new History(), () => options,
            _ => 10, _ => { }, (_, _) => { }, registry);
        var origin = Assert.Single(state.Items);
        var frame = navigation.CaptureReturnPoint(state, origin);

        controller.LoadLocation(state, new PanelLocation(inner.SourceId, "/"), options);
        navigation.EnterNestedSource(state, PanelSide.Left, frame);
        Assert.Contains(state.Items, item => item.IsParentDirectory);

        registry.RemoveUnusedTemporarySources(inner.SourceId, PanelSourceId.Local, _ => { });
        Assert.True(registry.TryGetSource(outer.SourceId, out _));

        navigation.TryGoUp(state, PanelSide.Left);

        Assert.Equal(new PanelLocation(outer.SourceId, "/"), state.CurrentLocation);
        Assert.True(registry.TryGetSource(outer.SourceId, out _));
        Assert.Equal("container.bin", state.Items[state.CursorIndex].Name);
        registry.RemoveUnusedTemporarySources(outer.SourceId, PanelSourceId.Local, _ => { });
        Assert.False(registry.TryGetSource(inner.SourceId, out _));
    }

    [Fact]
    public void NestedReturns_RestoreEachPreviousSourceInLastInFirstOutOrder()
    {
        var outer = new InMemoryBatchFilePanelSource();
        var middle = new InMemoryBatchFilePanelSource();
        var inner = new InMemoryBatchFilePanelSource();
        outer.SeedFile("/first.container", "first");
        middle.SeedFile("/second.container", "second");
        inner.SeedFile("/file.txt", "inside");
        var registry = new FilePanelSourceRegistry([]);
        Assert.True(registry.TryAddOwned(outer));
        Assert.True(registry.TryAddOwned(middle));
        Assert.True(registry.TryAddOwned(inner));
        var controller = new PanelController(
            new PanelViewBuilder(new FakeFileSystemService(), new PanelSortService(), sources: registry));
        var state = new FilePanelState();
        var options = new AppSettings.PanelOptionsSettings();
        var navigation = new PanelNavigationService(controller, new History(), () => options,
            _ => 10, _ => { }, (_, _) => { }, registry);
        controller.LoadLocation(state, new PanelLocation(outer.SourceId, "/"), options);
        controller.SetSourceDisplay(state, "ZIP: outer.zip", showCurrentItemFullPath: true);
        var first = navigation.CaptureReturnPoint(state, Assert.Single(state.Items));
        controller.LoadLocation(state, new PanelLocation(middle.SourceId, "/"), options);
        navigation.EnterNestedSource(state, PanelSide.Left, first);
        controller.SetSourceDisplay(state, "ZIP: middle.zip", showCurrentItemFullPath: true);
        var second = navigation.CaptureReturnPoint(state, Assert.Single(state.Items.Where(item => !item.IsParentDirectory)));
        controller.LoadLocation(state, new PanelLocation(inner.SourceId, "/"), options);
        navigation.EnterNestedSource(state, PanelSide.Left, second);
        controller.SetSourceDisplay(state, "7Z: inner.7z", showCurrentItemFullPath: true);

        navigation.TryGoUp(state, PanelSide.Left);
        Assert.Equal(middle.SourceId, state.SourceId);
        Assert.Equal("ZIP: middle.zip", state.DisplayTitle);
        Assert.Equal("/", state.DisplayTitleBasePath);
        Assert.Equal("second.container", state.Items[state.CursorIndex].Name);

        navigation.TryGoUp(state, PanelSide.Left);
        Assert.Equal(outer.SourceId, state.SourceId);
        Assert.Equal("ZIP: outer.zip", state.DisplayTitle);
        Assert.Equal("/", state.DisplayTitleBasePath);
        Assert.True(state.ShowCurrentItemFullPath);
        Assert.Equal("first.container", state.Items[state.CursorIndex].Name);
    }

    private sealed class History : IHistoryStore
    {
        public IReadOnlyList<CommandHistoryItem> GetCommandHistory() => [];
        public void AddCommand(CommandHistoryItem item) { }
        public bool RemoveCommand(string command) => false;
        public IReadOnlyList<DirectoryHistoryItem> GetDirectoryHistory() => [];
        public void AddDirectory(DirectoryHistoryItem item) { }
        public IReadOnlyList<FileHistoryItem> GetFileHistory() => [];
        public void AddFile(FileHistoryItem item) { }
    }
}
