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
        File.WriteAllText(path, "initial");
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => requested.TrySetResult());
        monitor.Enable(_root);

        monitor.RecordChange(DirectoryChangeKind.Changed, path, null);
        monitor.RecordChange(DirectoryChangeKind.Changed, path, null);

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(monitor.GetRecentChanges());
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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
