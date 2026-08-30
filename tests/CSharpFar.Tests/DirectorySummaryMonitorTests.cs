using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class DirectorySummaryMonitorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarMonitor_{Guid.NewGuid():N}");

    public DirectorySummaryMonitorTests() => Directory.CreateDirectory(_root);

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
