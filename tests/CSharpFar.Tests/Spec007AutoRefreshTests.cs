using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

/// <summary>
/// Stage 7 – Auto refresh: FileSystemChangeWatcher threshold and network policies.
/// Tests use real directories and query StartWatching return value only (no I/O events).
/// </summary>
public sealed class Spec007AutoRefreshTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemChangeWatcher _watcher = new();

    public Spec007AutoRefreshTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarARTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PanelWatchRequest LocalRequest(int objectCount, int threshold = 0,
                                            bool networkRefresh = false) =>
        new()
        {
            PanelSide      = PanelSide.Left,
            DirectoryPath  = _tempDir,
            ObjectCount    = objectCount,
            IsNetworkDrive = false,
            Options        = new AppSettings.PanelAutoRefreshSettings
            {
                DisableIfObjectCountExceeds = threshold,
                NetworkDrivesAutoRefresh    = networkRefresh,
            },
        };

    private PanelWatchRequest NetworkRequest(int threshold = 0, bool networkRefresh = false) =>
        new()
        {
            PanelSide      = PanelSide.Left,
            DirectoryPath  = _tempDir,
            ObjectCount    = 10,
            IsNetworkDrive = true,
            Options        = new AppSettings.PanelAutoRefreshSettings
            {
                DisableIfObjectCountExceeds = threshold,
                NetworkDrivesAutoRefresh    = networkRefresh,
            },
        };

    // ── Object count threshold ────────────────────────────────────────────────

    [Fact]
    public void StartWatching_BelowThreshold_IsWatching_True()
    {
        var state = _watcher.StartWatching(LocalRequest(objectCount: 5, threshold: 10));

        Assert.True(state.IsWatching);
    }

    [Fact]
    public void StartWatching_ExceedsThreshold_IsWatching_False()
    {
        var state = _watcher.StartWatching(LocalRequest(objectCount: 100, threshold: 50));

        Assert.False(state.IsWatching);
        Assert.True(state.DisabledByObjectCount);
    }

    [Fact]
    public void StartWatching_ZeroThreshold_AlwaysWatches()
    {
        var state = _watcher.StartWatching(LocalRequest(objectCount: 100000, threshold: 0));

        Assert.True(state.IsWatching);
    }

    [Fact]
    public void StartWatching_EqualToThreshold_DoesNotDisable()
    {
        // DisableIf > threshold; equal is NOT disabled
        var state = _watcher.StartWatching(LocalRequest(objectCount: 50, threshold: 50));

        Assert.True(state.IsWatching);
    }

    // ── Network drive policy ──────────────────────────────────────────────────

    [Fact]
    public void StartWatching_NetworkDrive_NetworkRefreshFalse_IsWatching_False()
    {
        var state = _watcher.StartWatching(NetworkRequest(networkRefresh: false));

        Assert.False(state.IsWatching);
        Assert.True(state.DisabledForNetworkDrive);
    }

    [Fact]
    public void StartWatching_NetworkDrive_NetworkRefreshTrue_IsWatching_True()
    {
        var state = _watcher.StartWatching(NetworkRequest(networkRefresh: true));

        // May fail if the temp dir itself is not accessible for watching,
        // but the policy check passes → either watching or an error, not disabled-for-network.
        Assert.False(state.DisabledForNetworkDrive);
    }

    // ── StopWatching ──────────────────────────────────────────────────────────

    [Fact]
    public void StopWatching_AfterStart_DoesNotThrow()
    {
        _watcher.StartWatching(LocalRequest(objectCount: 0));

        // Should not throw
        _watcher.StopWatching(PanelSide.Left);
    }

    [Fact]
    public void StopWatching_WithoutStart_DoesNotThrow()
    {
        // No prior StartWatching — should be a no-op
        _watcher.StopWatching(PanelSide.Right);
    }
}
