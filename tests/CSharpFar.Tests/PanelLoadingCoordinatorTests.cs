using System.Collections.Concurrent;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed class PanelLoadingCoordinatorTests
{
    [Fact]
    public void SlowLoadDoesNotChangeCommittedViewAndOtherPanelCanComplete()
    {
        using var f = new Fixture();
        var left = f.Left;
        var right = f.Right;
        f.Navigate(left, "/slow");
        var slow = f.Take(f.Source);
        Assert.NotEqual(Environment.CurrentManagedThreadId, slow.ThreadId);
        Assert.True(left.IsLoading);
        Assert.Equal("/", left.SourcePath);
        Assert.Contains(left.Items, i => i.Name == "initial.txt");
        f.Navigate(right, "/fast");
        var fast = f.Take(f.Source);
        fast.Succeed();
        f.PumpUntil(() => !right.IsLoading);
        Assert.Equal("/fast", right.SourcePath);
        Assert.True(left.IsLoading);
        Assert.Equal("/", left.SourcePath);
        // Completion belongs to the logical panel, regardless of screen side.
        (left, right) = (right, left);
        slow.Succeed();
        f.PumpUntil(() => !right.IsLoading);
        Assert.Equal("/slow", right.SourcePath);
        Assert.Equal("/fast", left.SourcePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OlderCompletionCannotReplaceNewerViewOrReportStaleError(bool fail)
    {
        using var f = new Fixture();
        f.Navigate(f.Left, "/old");
        var old = f.Take(f.Source);
        f.Navigate(f.Left, "/new");
        var newer = f.Take(f.Source);
        Assert.True(old.Token.IsCancellationRequested);
        newer.Succeed();
        f.PumpUntil(() => !f.Left.IsLoading);
        if (fail) old.Fail(); else old.Succeed();
        f.PumpUntil(() => f.UiCompletionCount >= 2);
        Assert.Equal("/new", f.Left.SourcePath);
        Assert.Null(f.Left.LoadError);
    }

    [Fact]
    public void CancelRetainsCommittedViewEvenIfProviderIgnoresCancellation()
    {
        using var f = new Fixture();
        f.Navigate(f.Left, "/slow");
        var slow = f.Take(f.Source);
        Assert.True(f.Loader.Cancel(f.Left));
        Assert.True(slow.Token.IsCancellationRequested);
        Assert.False(f.Left.IsLoading);
        Assert.False(f.Left.IsLoadingIndicatorVisible);
        slow.Succeed();
        f.PumpUntil(() => f.UiCompletionCount >= 1);
        Assert.Equal("/", f.Left.SourcePath);
        Assert.Contains(f.Left.Items, i => i.Name == "initial.txt");
    }

    [Fact]
    public void CurrentErrorIsVisibleAndRetrySucceeds()
    {
        using var f = new Fixture();
        f.Navigate(f.Left, "/broken");
        f.Take(f.Source).Fail();
        f.PumpUntil(() => !f.Left.IsLoading);
        Assert.NotNull(f.Left.LoadError);
        f.Navigate(f.Left, "/broken");
        f.Take(f.Source).Succeed();
        f.PumpUntil(() => !f.Left.IsLoading);
        Assert.Null(f.Left.LoadError);
        Assert.Equal("/broken", f.Left.SourcePath);
    }

    [Fact]
    public void NavigationWinsOverRefreshAndOnlyLatestPendingNavigationStarts()
    {
        using var f = new Fixture();
        f.Loader.Refresh(f.Left, f.Options, () => 10);
        var refresh = f.Take(f.Source);
        f.Navigate(f.Left, "/a");
        var a = f.Take(f.Source);
        for (int i = 0; i < 30; i++)
        {
            f.Navigate(f.Left, "/pending" + i);
            f.Loader.Refresh(f.Left, f.Options, () => 10);
        }
        Assert.Equal(2, f.Source.StartCount);
        refresh.Succeed();
        f.PumpUntil(() => f.Source.StartCount == 3);
        var last = f.Take(f.Source);
        Assert.Equal("/pending29", last.Path);
        last.Succeed();
        f.PumpUntil(() => !f.Left.IsLoading);
        a.Succeed();
        f.PumpUntil(() => f.UiCompletionCount >= 3);
        Assert.Equal("/pending29", f.Left.SourcePath);
        Assert.Equal(3, f.Source.StartCount);
    }

    [Fact]
    public void RepeatedRefreshCoalescesAndRetainsCurrentSelection()
    {
        using var f = new Fixture();
        var original = f.Left.Items.Single(i => i.Name == "initial.txt");
        f.Left.SelectedLocations.Add(original.Location);
        f.Left.CursorIndex = f.Left.Items.IndexOf(original);
        f.Loader.Refresh(f.Left, f.Options, () => 10);
        var first = f.Take(f.Source);
        for (int i = 0; i < 50; i++) f.Loader.Refresh(f.Left, f.Options, () => 10);
        Assert.Equal(1, f.Source.StartCount);
        first.Succeed(original);
        f.PumpUntil(() => f.Source.StartCount == 2);
        f.Take(f.Source).Succeed(original);
        f.PumpUntil(() => !f.Left.IsLoading);
        Assert.Equal(2, f.Source.StartCount);
        Assert.Contains(original.Location, f.Left.SelectedLocations);
        Assert.Equal(original.Location, f.Left.Items[f.Left.CursorIndex].Location);
    }

    [Fact]
    public void SourceSwitchAndShutdownKeepLeaseUntilActualCompletion()
    {
        using var f = new Fixture();
        var temporary = new ControlledSource(new PanelSourceId("temporary"));
        Assert.True(f.Registry.TryAddOwned(temporary));
        f.Loader.Navigate(f.Left, new PanelLocation(temporary.SourceId, "/temp"), f.Options);
        var pending = f.Take(temporary);
        using var released = new ManualResetEventSlim();
        f.Registry.RetireOwnedSource(temporary, _ => released.Set());
        f.Navigate(f.Left, "/replacement");
        f.Take(f.Source).Succeed();
        f.PumpUntil(() => !f.Left.IsLoading);
        Assert.Equal(f.Source.SourceId, f.Left.SourceId);
        Assert.False(released.IsSet);
        f.Loader.Dispose();
        pending.Succeed();
        Assert.True(released.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(f.Registry.TryGetSource(temporary.SourceId, out _));
        Assert.Equal("/replacement", f.Left.SourcePath);
    }

    [Fact]
    public void ShutdownCancelsActiveWorkWithoutWaitingForUncooperativeSource()
    {
        using var f = new Fixture();
        f.Navigate(f.Left, "/slow");
        var slow = f.Take(f.Source);
        f.Loader.Dispose();
        Assert.True(slow.Token.IsCancellationRequested);
        Assert.False(f.Left.IsLoading);
        Assert.Equal("/", f.Left.SourcePath);
        slow.Succeed();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ConcurrentQueue<Action> _ui = new();
        private readonly List<Call> _calls = [];
        public ControlledSource Source { get; } = new(new PanelSourceId("controlled"));
        public FilePanelSourceRegistry Registry { get; }
        public PanelLoadingCoordinator Loader { get; }
        public FilePanelState Left { get; } = new();
        public FilePanelState Right { get; } = new();
        public AppSettings.PanelOptionsSettings Options { get; } = new();
        public int UiCompletionCount { get; private set; }

        public Fixture()
        {
            Registry = new FilePanelSourceRegistry([Source]);
            var controller = new PanelController(new PanelViewBuilder(
                new FakeFileSystemService(), new PanelSortService(), sources: Registry), sources: Registry);
            Source.SeedFile("/initial.txt", [1]);
            controller.LoadLocation(Left, new PanelLocation(Source.SourceId, "/"), Options);
            controller.LoadLocation(Right, new PanelLocation(Source.SourceId, "/"), Options);
            Loader = new PanelLoadingCoordinator(controller, Registry, _ui.Enqueue);
        }

        public void Navigate(FilePanelState panel, string path) =>
            Loader.Navigate(panel, new PanelLocation(Source.SourceId, path), Options);

        public Call Take(ControlledSource source)
        {
            Assert.True(source.Started.TryTake(out var call, TimeSpan.FromSeconds(10)), "Provider call did not start.");
            _calls.Add(call!);
            return call!;
        }

        public void PumpUntil(Func<bool> condition)
        {
            Assert.True(SpinWait.SpinUntil(() =>
            {
                while (_ui.TryDequeue(out var action))
                {
                    bool loading = Left.IsLoadingIndicatorVisible || Right.IsLoadingIndicatorVisible;
                    action();
                    // Indicator-only actions do not constitute completion.
                    if (loading == (Left.IsLoadingIndicatorVisible || Right.IsLoadingIndicatorVisible) ||
                        !Left.IsLoading || !Right.IsLoading)
                        UiCompletionCount++;
                }
                return condition();
            }, TimeSpan.FromSeconds(10)), "UI completion condition timed out.");
        }

        public void Dispose()
        {
            Loader.Dispose();
            foreach (var call in _calls) call.Succeed();
            while (Source.Started.TryTake(out var call)) call.Succeed();
        }
    }

    private sealed class ControlledSource(PanelSourceId id) : MemoryFilePanelView(id), IAsyncFilePanelSource
    {
        private int _startCount;
        public int StartCount => Volatile.Read(ref _startCount);
        public BlockingCollection<Call> Started { get; } = new();

        public Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(string path, CancellationToken token = default)
        {
            var call = new Call(path, token, Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref _startCount);
            Started.Add(call);
            // Deliberately ignore cancellation to exercise generation and lease protection.
            return call.Completion.Task;
        }

        public Task<FilePanelItem?> GetItemAsync(string path, CancellationToken token = default) =>
            Task.FromResult(GetItem(path, token));
    }

    private sealed record Call(string Path, CancellationToken Token, int ThreadId)
    {
        public TaskCompletionSource<IReadOnlyList<FilePanelItem>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Succeed(params FilePanelItem[] items) => Completion.TrySetResult(items);
        public void Fail() => Completion.TrySetException(new IOException("Controlled failure"));
    }
}
