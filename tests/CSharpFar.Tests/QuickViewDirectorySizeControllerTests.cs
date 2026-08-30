using CSharpFar.App.Viewer;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests;

public sealed class QuickViewDirectorySizeControllerTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(Path.GetTempPath(), $"CSharpFarQuickView_{Guid.NewGuid():N}");
    private string FirstDirectory => Path.Combine(_testRoot, "first");
    private string SecondDirectory => Path.Combine(_testRoot, "second");

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Fact]
    public void InactiveQuickView_RepeatedUpdatesDoNotWakeInputLoop()
    {
        int wakes = 0;
        using var controller = new QuickViewDirectorySizeController(() => wakes++);

        controller.Update(quickViewEnabled: false, item: null);
        controller.Update(quickViewEnabled: false, item: null);
        controller.Update(quickViewEnabled: false, item: null);

        Assert.Equal(0, wakes);
    }

    [Fact]
    public void InitialScanProgress_PublishesPartialStateAndWakesInput()
    {
        var calculator = new ControlledCalculator();
        int wakes = 0;
        using var controller = new QuickViewDirectorySizeController(() => wakes++, calculator);

        controller.Update(true, DirectoryItem(FirstDirectory));
        calculator.ReportProgress(1, FirstDirectory, new DirectorySizeState(10, false, []));

        Assert.Equal(new DirectorySizeState(10, false, []), controller.CurrentState);
        Assert.Equal(1, wakes);
    }

    [Fact]
    public void BackgroundScan_IgnoresProgressAndAtomicallyPublishesCompletion()
    {
        Directory.CreateDirectory(FirstDirectory);
        var calculator = new ControlledCalculator();
        using var controller = new QuickViewDirectorySizeController(() => { }, calculator);
        controller.Update(true, DirectoryItem(FirstDirectory));
        calculator.ReportCompleted(1, FirstDirectory, new DirectorySizeState(100, true, []));
        controller.ToggleMonitor();
        controller.RefreshMonitoredPath(FirstDirectory);

        calculator.ReportProgress(2, FirstDirectory, new DirectorySizeState(110, false, []));
        calculator.ReportProgress(2, FirstDirectory, new DirectorySizeState(120, false, []));

        Assert.Equal(new DirectorySizeState(100, true, []), controller.CurrentState);
        Assert.True(controller.IsBackgroundUpdating);

        calculator.ReportCompleted(2, FirstDirectory, new DirectorySizeState(130, true, []));
        calculator.ReportCompleted(2, FirstDirectory, new DirectorySizeState(140, true, []));

        Assert.Equal(new DirectorySizeState(130, true, []), controller.CurrentState);
        Assert.False(controller.IsBackgroundUpdating);
    }

    [Fact]
    public void SupersededOrCancelledScanCallbacks_AreIgnored()
    {
        Directory.CreateDirectory(FirstDirectory);
        Directory.CreateDirectory(SecondDirectory);
        var calculator = new ControlledCalculator();
        using var controller = new QuickViewDirectorySizeController(() => { }, calculator);
        controller.Update(true, DirectoryItem(FirstDirectory));
        controller.Update(true, DirectoryItem(SecondDirectory));

        calculator.ReportProgress(1, FirstDirectory, new DirectorySizeState(10, false, []));
        calculator.ReportCompleted(1, FirstDirectory, new DirectorySizeState(10, true, []));
        Assert.Null(controller.CurrentState);

        calculator.ReportCompleted(2, SecondDirectory, new DirectorySizeState(20, true, []));
        controller.ToggleMonitor();
        controller.RefreshMonitoredPath(SecondDirectory);
        controller.ToggleMonitor();
        controller.ToggleMonitor();
        calculator.ReportCompleted(3, SecondDirectory, new DirectorySizeState(30, true, []));

        Assert.Equal(new DirectorySizeState(20, true, []), controller.CurrentState);
        Assert.False(controller.IsBackgroundUpdating);
    }

    [Fact]
    public void Dispose_InvalidatesLateCallbacksWithoutWakingInput()
    {
        var calculator = new ControlledCalculator();
        int wakes = 0;
        var controller = new QuickViewDirectorySizeController(() => wakes++, calculator);
        controller.Update(true, DirectoryItem(FirstDirectory));
        controller.Dispose();

        calculator.ReportCompleted(1, FirstDirectory, new DirectorySizeState(10, true, []));

        Assert.Null(controller.CurrentState);
        Assert.Equal(0, wakes);
    }

    [Fact]
    public async Task MonitorOff_BetweenRefreshValidationAndStart_DoesNotStartBackgroundScan()
    {
        Directory.CreateDirectory(FirstDirectory);
        using var refreshValidated = new ManualResetEventSlim();
        using var resumeRefresh = new ManualResetEventSlim();
        var calculator = new ControlledCalculator();
        using var controller = new QuickViewDirectorySizeController(
            () => { }, calculator, () => { refreshValidated.Set(); resumeRefresh.Wait(); });
        controller.Update(true, DirectoryItem(FirstDirectory));
        controller.ToggleMonitor();

        Task refresh = Task.Run(() => controller.RefreshMonitoredPath(FirstDirectory));
        Assert.True(refreshValidated.Wait(TimeSpan.FromSeconds(5)));

        controller.ToggleMonitor();
        resumeRefresh.Set();
        await refresh;

        Assert.Equal(1, calculator.StartCount);
        Assert.False(controller.Monitor.IsEnabled);
        Assert.False(controller.IsBackgroundUpdating);
        Assert.Null(controller.CurrentState);
    }

    [Fact]
    public async Task Dispose_BetweenRefreshValidationAndStart_DoesNotStartOrMutateState()
    {
        Directory.CreateDirectory(FirstDirectory);
        using var refreshValidated = new ManualResetEventSlim();
        using var resumeRefresh = new ManualResetEventSlim();
        var calculator = new ControlledCalculator { ThrowOnStartAfterDispose = true };
        int wakes = 0;
        var controller = new QuickViewDirectorySizeController(
            () => wakes++, calculator, () => { refreshValidated.Set(); resumeRefresh.Wait(); });
        controller.Update(true, DirectoryItem(FirstDirectory));
        controller.ToggleMonitor();

        Task refresh = Task.Run(() => controller.RefreshMonitoredPath(FirstDirectory));
        Assert.True(refreshValidated.Wait(TimeSpan.FromSeconds(5)));

        controller.Dispose();
        resumeRefresh.Set();
        await refresh;

        Assert.Equal(1, calculator.StartCount);
        Assert.Equal(0, wakes);
        Assert.Null(controller.CurrentState);
        Assert.False(controller.IsBackgroundUpdating);
    }

    private static FilePanelItem DirectoryItem(string path) => new()
    {
        FullPath = path,
        Name = Path.GetFileName(path),
        IsDirectory = true,
    };

    private sealed class ControlledCalculator : IDirectorySizeCalculator
    {
        private long _nextOperationId;
        private bool _disposed;

        public event Action<DirectoryScanUpdate>? Progress;
        public event Action<DirectoryScanUpdate>? Completed;
        public int StartCount { get; private set; }
        public bool ThrowOnStartAfterDispose { get; init; }

        public long Start(string path, DirectoryScanProgressMode progressMode, Action<long>? operationStarted = null)
        {
            if (_disposed && ThrowOnStartAfterDispose)
                throw new ObjectDisposedException(nameof(ControlledCalculator));
            StartCount++;
            long operationId = ++_nextOperationId;
            operationStarted?.Invoke(operationId);
            return operationId;
        }

        public void Cancel() { }
        public void Dispose() => _disposed = true;

        public void ReportProgress(long operationId, string path, DirectorySizeState state) =>
            Progress?.Invoke(new DirectoryScanUpdate(operationId, path, state));

        public void ReportCompleted(long operationId, string path, DirectorySizeState state) =>
            Completed?.Invoke(new DirectoryScanUpdate(operationId, path, state));
    }
}
