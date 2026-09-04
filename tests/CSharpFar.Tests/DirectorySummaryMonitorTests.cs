using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class DirectorySummaryMonitorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarMonitor_{Guid.NewGuid():N}");

    public DirectorySummaryMonitorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Disable_IsIdempotentAndDoesNotWake()
    {
        int wakes = 0;
        using var monitor = new DirectorySummaryMonitor(() => wakes++, _ => { });

        monitor.Disable();
        monitor.Disable();

        Assert.Equal(0, wakes);
        Assert.Empty(monitor.GetRecentChanges());
    }

    [Fact]
    public async Task Disable_CancelsPendingRefresh()
    {
        int rescans = 0;
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => Interlocked.Increment(ref rescans));
        monitor.Enable(_root);
        monitor.RecordChange(DirectoryChangeKind.Changed, Path.Combine(_root, "app.log"), null);
        monitor.Disable();

        await Task.Delay(TimeSpan.FromMilliseconds(1200));

        Assert.Equal(0, rescans);
    }

    [Fact]
    public async Task StaleSessionCallback_DoesNotAffectNewMonitorSession()
    {
        string otherRoot = Path.Combine(Path.GetTempPath(), $"CSharpFarMonitor_{Guid.NewGuid():N}");
        Directory.CreateDirectory(otherRoot);
        try
        {
            int rescans = 0;
            using var monitor = new DirectorySummaryMonitor(() => { }, _ => Interlocked.Increment(ref rescans));
            monitor.Enable(_root);
            long oldGeneration = monitor.CurrentGeneration;
            monitor.Disable();
            monitor.Enable(otherRoot);

            monitor.RecordChange(oldGeneration, _root, DirectoryChangeKind.Changed, Path.Combine(_root, "stale.txt"), null);
            await Task.Delay(TimeSpan.FromMilliseconds(1200));

            Assert.Empty(monitor.GetRecentChanges());
            Assert.Equal(0, rescans);
        }
        finally
        {
            Directory.Delete(otherRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ChangeDuringScan_RequestsAnotherScanAfterCompletion()
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => requested.TrySetResult());
        monitor.Enable(_root);
        monitor.ScanStarted();
        monitor.RecordChange(DirectoryChangeKind.Changed, Path.Combine(_root, "changed-during-scan.txt"), null);
        monitor.ScanFinished();

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task DeduplicatedChangedEvent_StillRequestsSummaryRefresh()
    {
        string path = Path.Combine(_root, "app.log");
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => requested.TrySetResult());
        monitor.Enable(_root);

        monitor.RecordChange(DirectoryChangeKind.Changed, path, null);
        monitor.RecordChange(DirectoryChangeKind.Changed, path, null);

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(monitor.GetRecentChanges());
    }

    [Fact]
    public void RepeatedChanged_IsCollapsedAndKeepsItsId()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        monitor.Enable(_root);
        string path = Path.Combine(_root, "app.log");
        DateTimeOffset firstTime = new(2026, 8, 30, 22, 1, 10, TimeSpan.Zero);
        DateTimeOffset lastTime = firstTime.AddHours(1);

        monitor.RecordChange(DirectoryChangeKind.Changed, path, null, firstTime, tick: 1);
        long id = Assert.Single(monitor.GetRecentChanges()).Id;
        monitor.RecordChange(DirectoryChangeKind.Changed, path, null, firstTime.AddMinutes(1), tick: 60_000);
        monitor.RecordChange(DirectoryChangeKind.Changed, path, null, lastTime, tick: 3_600_000);

        DirectoryChange change = Assert.Single(monitor.GetRecentChanges());
        Assert.Equal(id, change.Id);
        Assert.Equal(3, change.RepeatCount);
        Assert.Equal(lastTime, change.Timestamp);
    }

    [Fact]
    public void ChangedWithinOneSecondOfCreated_IsAbsorbedByCreatedWithoutSlidingTheWindow()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        monitor.Enable(_root);
        string path = Path.Combine(_root, "new.txt");
        DateTimeOffset createdAt = DateTimeOffset.UnixEpoch;

        monitor.RecordChange(DirectoryChangeKind.Created, path, null, createdAt, tick: 100);
        long createdId = Assert.Single(monitor.GetRecentChanges()).Id;
        monitor.RecordChange(DirectoryChangeKind.Changed, path, null, createdAt.AddMilliseconds(400), tick: 500);
        monitor.RecordChange(DirectoryChangeKind.Changed, path, null, createdAt.AddMilliseconds(900), tick: 1000);

        DirectoryChange created = Assert.Single(monitor.GetRecentChanges());
        Assert.Equal(createdId, created.Id);
        Assert.Equal(DirectoryChangeKind.Created, created.Kind);
        Assert.Equal(1, created.RepeatCount);
        Assert.Equal(createdAt.AddMilliseconds(900), created.Timestamp);

        monitor.RecordChange(DirectoryChangeKind.Changed, path, null, createdAt.AddMilliseconds(1100), tick: 1200);
        Assert.Contains(monitor.GetRecentChanges(), change => change.Kind == DirectoryChangeKind.Changed);
    }

    [Fact]
    public void InterleavedChanged_IsCollapsedAndMovedToNewestPosition()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        monitor.Enable(_root);
        string a = Path.Combine(_root, "a.txt");
        string b = Path.Combine(_root, "b.txt");

        monitor.RecordChange(DirectoryChangeKind.Changed, a, null, DateTimeOffset.UnixEpoch, tick: 1);
        monitor.RecordChange(DirectoryChangeKind.Changed, b, null, DateTimeOffset.UnixEpoch.AddSeconds(1), tick: 2);
        monitor.RecordChange(DirectoryChangeKind.Changed, a, null, DateTimeOffset.UnixEpoch.AddSeconds(2), tick: 3);

        DirectoryChange[] changes = monitor.GetRecentChanges().ToArray();
        Assert.Collection(changes,
            change => { Assert.Equal("a.txt", change.RelativePath); Assert.Equal(2, change.RepeatCount); },
            change => { Assert.Equal("b.txt", change.RelativePath); Assert.Equal(1, change.RepeatCount); });
    }

    [Fact]
    public void Changed_RemainsSeparateFromCreatedDeletedAndRenamedEvents()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        monitor.Enable(_root);
        string oldPath = Path.Combine(_root, "old.txt");
        string newPath = Path.Combine(_root, "new.txt");

        monitor.RecordChange(DirectoryChangeKind.Created, oldPath, null);
        monitor.RecordChange(DirectoryChangeKind.Changed, oldPath, null);
        monitor.RecordChange(DirectoryChangeKind.Changed, oldPath, null);
        monitor.RecordChange(DirectoryChangeKind.Renamed, newPath, oldPath);
        monitor.RecordChange(DirectoryChangeKind.Changed, newPath, null);
        monitor.RecordChange(DirectoryChangeKind.Deleted, newPath, null);

        DirectoryChange[] changes = monitor.GetRecentChanges().ToArray();
        Assert.Equal(
            [DirectoryChangeKind.Deleted, DirectoryChangeKind.Changed, DirectoryChangeKind.Renamed, DirectoryChangeKind.Created],
            changes.Select(change => change.Kind));
        Assert.Equal(1, changes[1].RepeatCount);
        Assert.Equal(1, changes[3].RepeatCount);
    }

    [Fact]
    public void Changed_UsesLocalFilesystemPathCaseSemantics()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        monitor.Enable(_root);
        string path = Path.Combine(_root, "Case.txt");

        monitor.RecordChange(DirectoryChangeKind.Changed, path, null);
        monitor.RecordChange(DirectoryChangeKind.Changed, path.ToLowerInvariant(), null);

        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 2, monitor.GetRecentChanges().Count);
    }

    [Fact]
    public async Task CollapsedChangedDuringScan_StillRequestsFollowUpScan()
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => requested.TrySetResult());
        monitor.Enable(_root);
        string path = Path.Combine(_root, "app.log");
        monitor.RecordChange(DirectoryChangeKind.Changed, path, null);
        monitor.ScanStarted();

        monitor.RecordChange(DirectoryChangeKind.Changed, path, null);
        monitor.ScanFinished();

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, Assert.Single(monitor.GetRecentChanges()).RepeatCount);
    }

    [Fact]
    public void TargetResolution_UsesTheExactChangeAndRejectsDeletedEntries()
    {
        string existing = Path.Combine(_root, "existing.txt");
        File.WriteAllText(existing, "x");
        var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        using (monitor)
        {
            monitor.Enable(_root);
            monitor.RecordChange(DirectoryChangeKind.Created, existing, null);
            monitor.RecordChange(DirectoryChangeKind.Deleted, Path.Combine(_root, "gone.txt"), null);
            DirectoryChange[] changes = monitor.GetRecentChanges().ToArray();

            Assert.False(monitor.TryGetMonitorTarget(changes[0].Id, out _));
            Assert.True(monitor.TryGetMonitorTarget(changes[1].Id, out string target));
            Assert.Equal(existing, target);
        }
    }

    [Fact]
    public void RecentHistory_IsBoundedButKeepsMoreThanTenChanges()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        monitor.Enable(_root);

        for (int i = 0; i < 300; i++)
            monitor.RecordChange(DirectoryChangeKind.Created, Path.Combine(_root, $"item-{i}.txt"), null);

        DirectoryChange[] changes = monitor.GetRecentChanges().ToArray();
        Assert.Equal(256, changes.Length);
        Assert.Equal("item-299.txt", changes[0].RelativePath);
        Assert.Equal("item-44.txt", changes[^1].RelativePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
