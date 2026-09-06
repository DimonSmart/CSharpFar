using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

public sealed class DirectorySummaryMonitorResilienceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarMonitorResilience_{Guid.NewGuid():N}");

    public DirectorySummaryMonitorResilienceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task RecoverableRescanFailure_ReleasesScanStateAndAllowsLaterRefresh()
    {
        int attempts = 0;
        var firstAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DirectorySummaryMonitor? monitorRef = null;
        using var monitor = new DirectorySummaryMonitor(
            () => { },
            _ =>
            {
                int attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1)
                {
                    firstAttempt.TrySetResult(true);
                    throw new UnauthorizedAccessException("Access denied while refreshing monitored directory.");
                }

                monitorRef!.ScanFinished();
                secondAttempt.TrySetResult(true);
            });
        monitorRef = monitor;

        monitor.Enable(_root);
        monitor.RecordChange(DirectoryChangeKind.Created, Path.Combine(_root, "first.txt"), null);
        await firstAttempt.Task.WaitAsync(TimeSpan.FromSeconds(3));

        monitor.RecordChange(DirectoryChangeKind.Created, Path.Combine(_root, "second.txt"), null);
        await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(2, Volatile.Read(ref attempts));
        Assert.True(monitor.IsEnabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
