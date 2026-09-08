using System.Collections.Concurrent;
using CSharpFar.App.Panels;
using CSharpFar.App.Viewer;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;

namespace CSharpFar.Tests;

public sealed class PanelDirectorySizeCoordinatorTests
{
    [Fact]
    public void Calculate_DoesNotDuplicatePendingOrActiveOperation()
    {
        var source = new FakeSource("source-a");
        FilePanelItem item = Dir(source, "/root/a");
        FilePanelState left = State(source, "/root", item);
        FilePanelState right = State(source, "/other");
        var scanner = new ControlledScanner();
        ScanPlan plan = scanner.Plan("/root/a", block: true, finalSize: 10);
        using var coordinator = Coordinator([source], left, right, scanner);

        Assert.True(coordinator.Calculate(PanelSide.Left, left, item));
        Assert.False(coordinator.Calculate(PanelSide.Left, left, item));
        Assert.True(plan.Started.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(coordinator.Calculate(PanelSide.Left, left, item));

        plan.Release.Set();
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, item)?.State == PanelDirectorySizeState.Completed);
        Assert.Equal(1, scanner.CallCount("/root/a"));
    }

    [Fact]
    public void Refresh_PreservesLastCompletedValueUntilNewFinalResult()
    {
        var source = new FakeSource("source-a");
        FilePanelItem item = Dir(source, "/root/a");
        FilePanelState left = State(source, "/root", item);
        FilePanelState right = State(source, "/other");
        var scanner = new ControlledScanner();
        scanner.Plan("/root/a", block: false, finalSize: 100);
        using var coordinator = Coordinator([source], left, right, scanner);

        Assert.True(coordinator.Calculate(PanelSide.Left, left, item));
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, item)?.State == PanelDirectorySizeState.Completed);
        Assert.Equal(new PanelDirectorySizePresentation(100, false), coordinator.GetPresentation(PanelSide.Left, left, item));

        ScanPlan refresh = scanner.Plan("/root/a", block: true, finalSize: 120, progressSize: 20);
        Assert.True(coordinator.Calculate(PanelSide.Left, left, item));
        Assert.Equal(new PanelDirectorySizePresentation(100, true), coordinator.GetPresentation(PanelSide.Left, left, item));
        Assert.True(refresh.Started.Wait(TimeSpan.FromSeconds(2)));
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, item)?.CurrentPartialSize == 20);
        Assert.Equal(new PanelDirectorySizePresentation(100, true), coordinator.GetPresentation(PanelSide.Left, left, item));

        refresh.Release.Set();
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, item)?.State == PanelDirectorySizeState.Completed);
        Assert.Equal(new PanelDirectorySizePresentation(120, false), coordinator.GetPresentation(PanelSide.Left, left, item));
        Assert.Equal(2, scanner.CallCount("/root/a"));
    }

    [Fact]
    public void FailurePresentation_DistinguishesInitialFailureAndFailedRefresh()
    {
        var source = new FakeSource("source-a");
        FilePanelItem initial = Dir(source, "/root/initial");
        FilePanelItem refresh = Dir(source, "/root/refresh");
        FilePanelState left = State(source, "/root", initial, refresh);
        FilePanelState right = State(source, "/other");
        var scanner = new ControlledScanner();
        scanner.Plan("/root/initial", block: false, finalSize: 0, status: DirectoryTreeSizeCompletionStatus.Failed);
        scanner.Plan("/root/refresh", block: false, finalSize: 77);
        using var coordinator = Coordinator([source], left, right, scanner);

        coordinator.Calculate(PanelSide.Left, left, initial);
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, initial)?.State == PanelDirectorySizeState.Failed);
        Assert.Equal(new PanelDirectorySizePresentation(null, false), coordinator.GetPresentation(PanelSide.Left, left, initial));

        coordinator.Calculate(PanelSide.Left, left, refresh);
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, refresh)?.State == PanelDirectorySizeState.Completed);
        scanner.Plan("/root/refresh", block: false, finalSize: 0, status: DirectoryTreeSizeCompletionStatus.Failed);
        coordinator.Calculate(PanelSide.Left, left, refresh);
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, refresh)?.State == PanelDirectorySizeState.Failed);
        Assert.Equal(new PanelDirectorySizePresentation(77, false), coordinator.GetPresentation(PanelSide.Left, left, refresh));
    }

    [Fact]
    public void CalculateAll_UsesEligibleImmediateDirectoriesAndDoesNotDuplicateBatchWork()
    {
        var source = new FakeSource("source-a");
        FilePanelItem a = Dir(source, "/root/a");
        FilePanelItem b = Dir(source, "/root/b");
        FilePanelItem parent = new()
        {
            Name = "..",
            FullPath = "/",
            SourceId = source.SourceId,
            IsDirectory = true,
            IsParentDirectory = true,
        };
        FilePanelItem link = Dir(source, "/root/link", FileAttributes.ReparsePoint);
        FilePanelItem mount = Mount(source, "/root/mount");
        FilePanelItem file = File(source, "/root/file", 5);
        FilePanelState left = State(source, "/root", a, b, parent, link, mount, file);
        FilePanelState right = State(source, "/other");
        var scanner = new ControlledScanner();
        ScanPlan aPlan = scanner.Plan("/root/a", block: true, finalSize: 10);
        scanner.Plan("/root/b", block: false, finalSize: 20);
        using var coordinator = Coordinator([source], left, right, scanner);

        Assert.Equal(2, coordinator.CalculateAll(PanelSide.Left, left));
        Assert.True(aPlan.Started.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, coordinator.CalculateAll(PanelSide.Left, left));
        Assert.Equal(1, scanner.CallCount("/root/a"));
        Assert.Equal(0, scanner.CallCount("/root/b"));

        aPlan.Release.Set();
        WaitUntil(() => scanner.CallCount("/root/b") == 1);
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, b)?.State == PanelDirectorySizeState.Completed);
        Assert.Equal(0, scanner.CallCount("/root/link"));
        Assert.Equal(0, scanner.CallCount("/root/mount"));
        Assert.Equal(0, scanner.CallCount("/root/file"));
        Assert.Equal(1, scanner.MaxActive);
    }

    [Fact]
    public void Navigation_InvalidatesSessionCancelsActiveAndDropsQueuedWork()
    {
        var source = new FakeSource("source-a");
        FilePanelItem a = Dir(source, "/root/a");
        FilePanelItem b = Dir(source, "/root/b");
        FilePanelState left = State(source, "/root", a, b);
        FilePanelState right = State(source, "/other");
        var scanner = new ControlledScanner();
        ScanPlan active = scanner.Plan("/root/a", block: true, finalSize: 10);
        scanner.Plan("/root/b", block: false, finalSize: 20);
        using var coordinator = Coordinator([source], left, right, scanner);

        coordinator.CalculateAll(PanelSide.Left, left);
        Assert.True(active.Started.Wait(TimeSpan.FromSeconds(2)));

        left.CurrentLocation = new PanelLocation(source.SourceId, "/child");
        Assert.Null(coordinator.GetPresentation(PanelSide.Left, left, a));
        coordinator.Reconcile(PanelSide.Left, left);
        Assert.True(active.Cancelled.Wait(TimeSpan.FromSeconds(2)));
        active.Release.Set();
        Thread.Sleep(50);

        Assert.Equal(0, scanner.CallCount("/root/b"));
        Assert.Null(coordinator.GetSnapshot(PanelSide.Left, left, a));
    }

    [Fact]
    public void Reconcile_PreservesStableIdentityButDropsRemovedOrRenamedDirectories()
    {
        var source = new FakeSource("source-a");
        FilePanelItem a = Dir(source, "/root/a");
        FilePanelItem old = Dir(source, "/root/old");
        FilePanelState left = State(source, "/root", a, old);
        FilePanelState right = State(source, "/other");
        var scanner = new ControlledScanner();
        scanner.Plan("/root/a", block: false, finalSize: 30);
        scanner.Plan("/root/old", block: false, finalSize: 42);
        using var coordinator = Coordinator([source], left, right, scanner);
        coordinator.Calculate(PanelSide.Left, left, a);
        coordinator.Calculate(PanelSide.Left, left, old);
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, a)?.State == PanelDirectorySizeState.Completed);
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, old)?.State == PanelDirectorySizeState.Completed);

        FilePanelItem replacementA = Dir(source, "/root/a");
        FilePanelItem renamed = Dir(source, "/root/new");
        left.Items.Clear();
        left.Items.AddRange([renamed, replacementA]);
        coordinator.Reconcile(PanelSide.Left, left);

        Assert.Equal(new PanelDirectorySizePresentation(30, false), coordinator.GetPresentation(PanelSide.Left, left, replacementA));
        Assert.Null(coordinator.GetSnapshot(PanelSide.Left, left, old));
        Assert.Null(coordinator.GetPresentation(PanelSide.Left, left, renamed));
    }

    [Fact]
    public void LeftAndRightSessions_AreIndependent()
    {
        var leftSource = new FakeSource("left-source");
        var rightSource = new FakeSource("right-source");
        FilePanelItem leftItem = Dir(leftSource, "/left/a");
        FilePanelItem rightItem = Dir(rightSource, "/right/b");
        FilePanelState left = State(leftSource, "/left", leftItem);
        FilePanelState right = State(rightSource, "/right", rightItem);
        var scanner = new ControlledScanner();
        ScanPlan leftPlan = scanner.Plan("/left/a", block: true, finalSize: 10);
        ScanPlan rightPlan = scanner.Plan("/right/b", block: true, finalSize: 20);
        using var coordinator = Coordinator([leftSource, rightSource], left, right, scanner);

        coordinator.Calculate(PanelSide.Left, left, leftItem);
        coordinator.Calculate(PanelSide.Right, right, rightItem);
        Assert.True(leftPlan.Started.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(rightPlan.Started.Wait(TimeSpan.FromSeconds(2)));

        left.CurrentLocation = new PanelLocation(leftSource.SourceId, "/left/child");
        coordinator.Reconcile(PanelSide.Left, left);
        Assert.True(leftPlan.Cancelled.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(rightPlan.Cancelled.IsSet);
        Assert.Equal(new PanelDirectorySizePresentation(null, true), coordinator.GetPresentation(PanelSide.Right, right, rightItem));

        rightPlan.Release.Set();
        leftPlan.Release.Set();
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Right, right, rightItem)?.State == PanelDirectorySizeState.Completed);
        Assert.Equal(new PanelDirectorySizePresentation(20, false), coordinator.GetPresentation(PanelSide.Right, right, rightItem));
    }

    [Fact]
    public void ProviderGate_BoundsConcurrencyAcrossPanelsSharingSource()
    {
        var source = new FakeSource("shared");
        FilePanelItem leftItem = Dir(source, "/left/a");
        FilePanelItem rightItem = Dir(source, "/right/b");
        FilePanelState left = State(source, "/left", leftItem);
        FilePanelState right = State(source, "/right", rightItem);
        var scanner = new ControlledScanner();
        ScanPlan first = scanner.Plan("/left/a", block: true, finalSize: 1);
        ScanPlan second = scanner.Plan("/right/b", block: false, finalSize: 2);
        using var coordinator = Coordinator([source], left, right, scanner);

        coordinator.Calculate(PanelSide.Left, left, leftItem);
        Assert.True(first.Started.Wait(TimeSpan.FromSeconds(2)));
        coordinator.Calculate(PanelSide.Right, right, rightItem);
        Thread.Sleep(50);
        Assert.False(second.Started.IsSet);

        first.Release.Set();
        Assert.True(second.Started.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, scanner.MaxActive);
    }

    [Fact]
    public void VirtualPanel_IsNotEligibleAndCalculatedSizeDoesNotMutateItemMetadata()
    {
        var source = new FakeSource("source-a");
        FilePanelItem item = Dir(source, "/root/a");
        FilePanelState left = State(source, "/root", item);
        FilePanelState right = State(source, "/other");
        var scanner = new ControlledScanner();
        scanner.Plan("/root/a", block: false, finalSize: 123);
        using var coordinator = Coordinator([source], left, right, scanner);

        coordinator.Calculate(PanelSide.Left, left, item);
        WaitUntil(() => coordinator.GetSnapshot(PanelSide.Left, left, item)?.State == PanelDirectorySizeState.Completed);
        Assert.Null(item.Size);
        Assert.Equal(123, coordinator.GetPresentation(PanelSide.Left, left, item)?.DisplaySize);

        left.ContentKind = PanelContentKind.Virtual;
        Assert.False(coordinator.CanCalculateAll(left));
        Assert.Equal(0, coordinator.CalculateAll(PanelSide.Left, left));
    }

    private static PanelDirectorySizeCoordinator Coordinator(
        IEnumerable<FakeSource> sources,
        FilePanelState left,
        FilePanelState right,
        ControlledScanner scanner) =>
        new(new FilePanelSourceRegistry(sources), left, right, () => { }, scanner);

    private static FilePanelState State(FakeSource source, string path, params FilePanelItem[] items)
    {
        var state = new FilePanelState
        {
            ProviderCapabilities = PanelProviderCapabilities.Enumerate,
            ContentKind = PanelContentKind.Source,
        };
        state.CurrentLocation = new PanelLocation(source.SourceId, path);
        state.Items.AddRange(items);
        return state;
    }

    private static FilePanelItem Dir(FakeSource source, string path, FileAttributes extra = 0) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = source.SourceId,
        IsDirectory = true,
        Size = null,
        Attributes = FileAttributes.Directory | extra,
    };

    private static FilePanelItem Mount(FakeSource source, string path) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = source.SourceId,
        IsDirectory = true,
        Size = null,
        Attributes = FileAttributes.Directory,
        IsVolumeMountPoint = true,
    };

    private static FilePanelItem File(FakeSource source, string path, long size) => new()
    {
        Name = Name(path),
        FullPath = path,
        SourceId = source.SourceId,
        IsDirectory = false,
        Size = size,
        Attributes = FileAttributes.Normal,
    };

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static void WaitUntil(Func<bool> condition) =>
        Assert.True(
            SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(3)),
            "Timed out waiting for background directory-size work.");

    private sealed class FakeSource : IFilePanelSource
    {
        public FakeSource(string id) => SourceId = new PanelSourceId(id);

        public PanelSourceId SourceId { get; }
        public string DisplayName => SourceId.Value;
        public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.Enumerate;
        public IReadOnlyCollection<char> PathSeparators => ['/'];
        public string NormalizePath(string sourcePath)
        {
            string path = sourcePath.TrimEnd('/');
            return path.Length == 0 ? "/" : path;
        }
        public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "/";
        public string? GetParentPath(string sourcePath) => null;
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string sourcePath, CancellationToken cancellationToken = default) => [];
        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) => null;
        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ControlledScanner : IDirectoryTreeSizeScanner
    {
        private readonly ConcurrentDictionary<string, ConcurrentQueue<ScanPlan>> _plans = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);
        private int _active;
        private int _maxActive;

        public int MaxActive => Volatile.Read(ref _maxActive);
        public int CallCount(string path) => _calls.TryGetValue(path, out int count) ? count : 0;

        public ScanPlan Plan(
            string path,
            bool block,
            long finalSize,
            long? progressSize = null,
            DirectoryTreeSizeCompletionStatus status = DirectoryTreeSizeCompletionStatus.Completed)
        {
            var plan = new ScanPlan(block, finalSize, progressSize, status);
            _plans.GetOrAdd(path, _ => new ConcurrentQueue<ScanPlan>()).Enqueue(plan);
            return plan;
        }

        public DirectoryTreeSizeScanResult Scan(
            IFilePanelSource source,
            string rootSourcePath,
            Action<DirectoryTreeSizeProgress>? progress,
            CancellationToken cancellationToken)
        {
            _calls.AddOrUpdate(rootSourcePath, 1, (_, value) => value + 1);
            int active = Interlocked.Increment(ref _active);
            UpdateMax(active);
            try
            {
                if (!_plans.TryGetValue(rootSourcePath, out ConcurrentQueue<ScanPlan>? plans) ||
                    !plans.TryDequeue(out ScanPlan? plan))
                {
                    plan = new ScanPlan(false, 0, null, DirectoryTreeSizeCompletionStatus.Completed);
                }

                plan.Started.Set();
                if (plan.ProgressSize is { } partial)
                    progress?.Invoke(new DirectoryTreeSizeProgress(partial, []));

                if (plan.Block)
                {
                    int signalled = WaitHandle.WaitAny(
                        [plan.Release.WaitHandle, cancellationToken.WaitHandle],
                        TimeSpan.FromSeconds(5));
                    if (signalled == 1)
                    {
                        plan.Cancelled.Set();
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (signalled == WaitHandle.WaitTimeout)
                        throw new TimeoutException("Controlled scanner was not released.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new DirectoryTreeSizeScanResult(
                    plan.FinalSize,
                    plan.Status,
                    plan.Status == DirectoryTreeSizeCompletionStatus.Completed ? [] : ["planned error"]);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMax(int active)
        {
            while (true)
            {
                int observed = Volatile.Read(ref _maxActive);
                if (active <= observed || Interlocked.CompareExchange(ref _maxActive, active, observed) == observed)
                    return;
            }
        }
    }

    private sealed class ScanPlan
    {
        public ScanPlan(bool block, long finalSize, long? progressSize, DirectoryTreeSizeCompletionStatus status)
        {
            Block = block;
            FinalSize = finalSize;
            ProgressSize = progressSize;
            Status = status;
        }

        public bool Block { get; }
        public long FinalSize { get; }
        public long? ProgressSize { get; }
        public DirectoryTreeSizeCompletionStatus Status { get; }
        public ManualResetEventSlim Started { get; } = new(false);
        public ManualResetEventSlim Release { get; } = new(false);
        public ManualResetEventSlim Cancelled { get; } = new(false);
    }
}
