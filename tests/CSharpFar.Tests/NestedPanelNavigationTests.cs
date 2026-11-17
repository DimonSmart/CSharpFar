using System.Collections.Concurrent;
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
    public async Task AsyncNestedReturn_RestoresOriginalPanelOnlyAfterUiCompletion()
    {
        var outer = new InMemoryBatchFilePanelSource();
        var inner = new InMemoryBatchFilePanelSource();
        outer.SeedFile("/container.bin", "container");
        inner.SeedFile("/inside.txt", "inside");
        var registry = new FilePanelSourceRegistry([]);
        Assert.True(registry.TryAddOwned(outer));
        Assert.True(registry.TryAddOwned(inner));
        var controller = new PanelController(
            new PanelViewBuilder(new FakeFileSystemService(), new PanelSortService(), sources: registry));
        var options = new AppSettings.PanelOptionsSettings();
        var queue = new ConcurrentQueue<Action>();
        var state = new FilePanelState();
        var navigation = new PanelNavigationService(controller, new History(),
            () => options, _ => 10, _ => { }, (_, _) => { }, registry)
        {
            AsyncLoads = new AsyncPluginPanelLoader(controller, registry, queue.Enqueue)
        };

        controller.LoadLocation(state, new PanelLocation(outer.SourceId, "/"));
        var frame = navigation.CaptureReturnPoint(state, Assert.Single(state.Items));
        controller.LoadLocation(state, new PanelLocation(inner.SourceId, "/"));
        navigation.EnterNestedSource(state, PanelSide.Left, frame);
        Assert.Contains(state.Items, item => item.IsParentDirectory);

        navigation.TryGoUp(state, PanelSide.Left);
        Assert.Equal(inner.SourceId, state.SourceId);
        await PumpUntilAsync(queue, () => state.SourceId == outer.SourceId);
        Assert.Equal("container.bin", state.Items[state.CursorIndex].Name);
        Assert.True(registry.TryGetSource(outer.SourceId, out _));
    }

    [Fact]
    public async Task AsyncDirectoryEnterAndUp_KeepPreviousLocationUntilCommit()
    {
        var source = new InMemoryBatchFilePanelSource();
        source.SeedDirectory("/child");
        source.SeedFile("/child/file.txt", "contents");
        var registry = new FilePanelSourceRegistry([]);
        Assert.True(registry.TryAddOwned(source));
        var controller = new PanelController(
            new PanelViewBuilder(new FakeFileSystemService(), new PanelSortService(), sources: registry));
        var options = new AppSettings.PanelOptionsSettings();
        var queue = new ConcurrentQueue<Action>();
        var state = new FilePanelState();
        var navigation = new PanelNavigationService(controller, new History(),
            () => options, _ => 10, _ => { }, (_, _) => { }, registry)
        {
            AsyncLoads = new AsyncPluginPanelLoader(controller, registry, queue.Enqueue)
        };
        controller.LoadLocation(state, new PanelLocation(source.SourceId, "/"));
        var child = Assert.Single(state.Items, item => item.Name == "child");

        navigation.OpenDirectoryItem(state, PanelSide.Right, child);
        Assert.Equal("/", state.SourcePath);
        await PumpUntilAsync(queue, () => state.SourcePath == "/child");
        Assert.Contains(state.Items, item => item.Name == "file.txt");

        navigation.TryGoUp(state, PanelSide.Right);
        Assert.Equal("/child", state.SourcePath);
        await PumpUntilAsync(queue, () => state.SourcePath == "/");
        Assert.Equal("child", state.Items[state.CursorIndex].Name);
    }

    private static async Task PumpUntilAsync(ConcurrentQueue<Action> actions, Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            while (actions.TryDequeue(out var action)) action();
            if (predicate()) return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Expected asynchronous navigation was not completed.");
            await Task.Delay(10);
        }
    }

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
        var second = navigation.CaptureReturnPoint(state, Assert.Single(state.Items, item => !item.IsParentDirectory));
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
