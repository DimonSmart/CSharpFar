using System.Collections.Concurrent;
using CSharpFar.App.Panels;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class AsyncPluginPanelLoaderTests
{
    [Fact]
    public async Task PendingAsyncEnumeration_DoesNotBlockUiOrMutatePanelBeforeCompletion()
    {
        var source = new DeferredSource();
        source.SeedDirectory("/slow");
        source.SeedFile("/slow/result.txt", "payload");
        var fixture = new Fixture(source);
        var original = fixture.State.CurrentLocation;
        fixture.State.Items.Add(new FilePanelItem { Name = "old.txt", FullPath = "/old.txt", IsDirectory = false });

        fixture.Loader.Load(fixture.State, new PanelLocation(source.SourceId, "/slow"), new(),
            onSuccess: () => fixture.Applied++);
        await source.WaitForRequestAsync("/slow");

        Assert.Equal(original, fixture.State.CurrentLocation);
        Assert.Equal("old.txt", Assert.Single(fixture.State.Items).Name);
        Assert.Equal(0, fixture.Applied);

        source.Release("/slow");
        await fixture.PumpUntilAsync(() => fixture.Applied == 1);
        Assert.Equal(new PanelLocation(source.SourceId, "/slow"), fixture.State.CurrentLocation);
        Assert.Contains(fixture.State.Items, item => item.Name == "result.txt");
    }

    [Fact]
    public async Task SupersededEnumeration_IgnoresLateNonCooperativeResult()
    {
        var source = new DeferredSource();
        source.SeedDirectory("/older");
        source.SeedDirectory("/newer");
        source.SeedFile("/older/old.txt", "old");
        source.SeedFile("/newer/new.txt", "new");
        var fixture = new Fixture(source);
        int discarded = 0;

        fixture.Loader.Load(fixture.State, new PanelLocation(source.SourceId, "/older"), new(),
            onAbandoned: () => discarded++);
        await source.WaitForRequestAsync("/older");

        fixture.Loader.Load(fixture.State, new PanelLocation(source.SourceId, "/newer"), new(),
            onSuccess: () => fixture.Applied++);
        await source.WaitForRequestAsync("/newer");

        source.Release("/newer");
        await fixture.PumpUntilAsync(() => fixture.Applied == 1);
        Assert.Equal("/newer", fixture.State.SourcePath);

        source.Release("/older");
        await fixture.PumpUntilAsync(() => discarded == 1);
        Assert.Equal("/newer", fixture.State.SourcePath);
        Assert.Contains(fixture.State.Items, item => item.Name == "new.txt");
        Assert.DoesNotContain(fixture.State.Items, item => item.Name == "old.txt");
    }

    [Fact]
    public async Task SynchronousPanelReplacement_InvalidatesPendingLoadEvenWithSameOrigin()
    {
        var source = new DeferredSource();
        source.SeedDirectory("/late");
        var fixture = new Fixture(source);
        // Start on the same remote source so committing a new view leaves the
        // location unchanged; only ContentVersion can invalidate the old task.
        fixture.Controller.LoadLocation(fixture.State, new PanelLocation(source.SourceId, "/"));
        int discarded = 0;

        fixture.Loader.Load(fixture.State, new PanelLocation(source.SourceId, "/late"), new(),
            onAbandoned: () => discarded++);
        await source.WaitForRequestAsync("/late");
        var origin = fixture.State.CurrentLocation;
        var newView = new PanelView
        {
            Items = [new FilePanelItem { Name = "replacement.txt", FullPath = "/replacement.txt",
                IsDirectory = false }],
            Summary = new PanelSummary(),
            AutoRefreshState = new PanelAutoRefreshState(),
            IsRootDirectory = false,
        };
        fixture.Controller.CommitPreparedLocation(fixture.State, origin, newView);

        source.Release("/late");
        await fixture.PumpUntilAsync(() => discarded == 1);
        Assert.Equal(origin, fixture.State.CurrentLocation);
        Assert.Equal("replacement.txt", Assert.Single(fixture.State.Items).Name);
    }

    [Fact]
    public async Task FailedRefresh_PreservesCurrentItemsSelectionAndCursor()
    {
        var source = new MemoryFilePanelView(new PanelSourceId("plugin:refresh"));
        source.SeedFile("/first.txt", "first");
        var fixture = new Fixture(source);
        fixture.Controller.LoadLocation(fixture.State, new PanelLocation(source.SourceId, "/"));
        fixture.State.SelectedPaths.Add("/first.txt");
        fixture.State.CursorIndex = 1;
        var previousItems = fixture.State.Items.ToArray();
        var previousCursor = fixture.State.CursorIndex;
        Exception? failure = null;

        source.FailEnumeratePath = "/";
        fixture.Loader.Refresh(fixture.State, visibleRows: 20, new(),
            onFailure: error => failure = error);
        await fixture.PumpUntilAsync(() => failure is not null);

        Assert.IsType<IOException>(failure);
        Assert.Equal(previousItems, fixture.State.Items);
        Assert.Contains("/first.txt", fixture.State.SelectedPaths);
        Assert.Equal(previousCursor, fixture.State.CursorIndex);
        Assert.Null(fixture.State.LoadError);
    }

    [Fact]
    public async Task Refresh_DoesNotDiscardChangesToSelectionWhileWaiting()
    {
        var source = new DeferredSource();
        source.SeedFile("/a.txt", "a");
        source.SeedFile("/b.txt", "b");
        var fixture = new Fixture(source);
        // A synchronous initial panel load is intentionally possible for existing APIs.
        fixture.Controller.LoadLocation(fixture.State, new PanelLocation(source.SourceId, "/"));
        fixture.State.SelectedPaths.Add("/a.txt");

        fixture.Loader.Refresh(fixture.State, visibleRows: 20, new(),
            onSuccess: () => fixture.Applied++);
        await source.WaitForRequestAsync("/");
        fixture.State.SelectedPaths.Add("/b.txt");
        source.Release("/");

        await fixture.PumpUntilAsync(() => fixture.Applied == 1);
        Assert.Contains("/a.txt", fixture.State.SelectedPaths);
        Assert.Contains("/b.txt", fixture.State.SelectedPaths);
        Assert.Equal(2, fixture.State.Summary!.SelectedCount);
    }

    [Fact]
    public void RetiringRejectedOwnedSource_IsDeferredUntilLastLeaseAndCanBeRescued()
    {
        var source = new MemoryFilePanelView(new PanelSourceId("plugin:owned"));
        var registry = new FilePanelSourceRegistry([]);
        Assert.True(registry.TryAddOwned(source));
        int disposed = 0;
        using (var lease = registry.AcquireUsage(source.SourceId))
        {
            Assert.True(registry.RetireOwnedWhenIdle(source.SourceId, source, _ => disposed++));
            Assert.Equal(0, disposed);
            registry.RetainSource(source.SourceId); // A second panel chose this source.
        }
        Assert.Equal(0, disposed);
        Assert.True(registry.TryGetSource(source.SourceId, out _));

        using (registry.AcquireUsage(source.SourceId))
            Assert.True(registry.RetireOwnedWhenIdle(source.SourceId, source, _ => disposed++));
        Assert.Equal(1, disposed);
        Assert.False(registry.TryGetSource(source.SourceId, out _));
    }

    [Fact]
    public async Task Shutdown_DoesNotBlockOnProviderIgnoringCancellationOrDisposeUsedSourceEarly()
    {
        var source = new DeferredSource();
        var fixture = new Fixture(source);
        fixture.Loader.Load(fixture.State, new PanelLocation(source.SourceId, "/"), new(),
            onSuccess: () => fixture.Applied++);
        await source.WaitForRequestAsync("/");
        fixture.Loader.Shutdown();

        int disposed = 0;
        fixture.Registry.DisposeOwnedSources(_ => Interlocked.Increment(ref disposed));
        Assert.Equal(0, disposed); // outstanding source usage lease defers disposal
        Assert.Equal(0, fixture.Applied);

        source.Release("/");
        await EventuallyAsync(() => Volatile.Read(ref disposed) == 1);
        fixture.Pump();
        Assert.Equal(0, fixture.Applied);
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Expected asynchronous plugin completion was not observed.");
            await Task.Delay(10);
        }
    }

    private sealed class Fixture
    {
        private readonly ConcurrentQueue<Action> _queue = new();
        public readonly FilePanelSourceRegistry Registry;
        public readonly PanelController Controller;
        public readonly FilePanelState State = new() { CurrentDirectory = "/original" };
        public readonly AsyncPluginPanelLoader Loader;
        public int Applied;

        public Fixture(IFilePanelSource source)
        {
            Registry = new FilePanelSourceRegistry([]);
            Assert.True(Registry.TryAddOwned(source));
            Controller = new PanelController(new PanelViewBuilder(new FakeFileSystemService(),
                new PanelSortService(), sources: Registry));
            Loader = new AsyncPluginPanelLoader(Controller, Registry, _queue.Enqueue);
        }

        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
                action();
        }

        public Task PumpUntilAsync(Func<bool> predicate) =>
            EventuallyAsync(() => { Pump(); return predicate(); });
    }

    private sealed class DeferredSource : MemoryFilePanelView, IAsyncFilePanelSource
    {
        private readonly ConcurrentDictionary<string,
            TaskCompletionSource<IReadOnlyList<FilePanelItem>>> _pending = new();

        public DeferredSource() : base(new PanelSourceId("plugin:deferred")) { }

        public Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(
            string sourcePath, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<FilePanelItem>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(sourcePath, completion))
                throw new InvalidOperationException("Duplicate deferred enumeration.");
            return completion.Task; // Deliberately ignores cancellation.
        }

        public async Task WaitForRequestAsync(string path) =>
            await EventuallyAsync(() => _pending.ContainsKey(path));

        public void Release(string path)
        {
            Assert.True(_pending.TryRemove(path, out var completion));
            completion.SetResult(EnumerateDirectory(path));
        }
    }
}
