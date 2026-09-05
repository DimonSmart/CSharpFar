using CSharpFar.App.Rendering;
using CSharpFar.App.Viewer;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class QuickViewMonitoringBehaviorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarQuickView_{Guid.NewGuid():N}");
    private readonly string _otherRoot = Path.Combine(Path.GetTempPath(), $"CSharpFarQuickView_{Guid.NewGuid():N}");

    public QuickViewMonitoringBehaviorTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_otherRoot);
    }

    [Fact]
    public void MonitoringPinsRootAcrossPanelNavigationAndDisableRetainsHistory()
    {
        string target = Path.Combine(_root, "changed.txt");
        File.WriteAllText(target, "changed");
        var calculator = new ControlledCalculator();
        using var controller = new QuickViewDirectorySizeController(() => { }, calculator);

        controller.Update(true, DirectoryItem(_root));
        controller.ToggleMonitor();
        controller.Monitor.RecordChange(DirectoryChangeKind.Changed, target, null);
        long changeId = Assert.Single(controller.Monitor.GetRecentChanges()).Id;

        controller.Update(true, DirectoryItem(_otherRoot));

        Assert.True(controller.Monitor.IsEnabled);
        Assert.True(controller.Monitor.IsHistoryFor(_root));
        Assert.False(controller.Monitor.IsHistoryFor(_otherRoot));
        Assert.Equal(1, calculator.StartCount);

        controller.ToggleMonitor();

        Assert.False(controller.Monitor.IsEnabled);
        Assert.Equal(changeId, Assert.Single(controller.Monitor.GetRecentChanges()).Id);

        controller.Update(true, FileItem(target));
        Assert.Null(controller.CurrentState);
        Assert.True(controller.Monitor.IsHistoryFor(_root));

        controller.Update(true, DirectoryItem(_root));
        controller.ToggleMonitor();

        Assert.True(controller.Monitor.IsEnabled);
        Assert.True(controller.Monitor.IsHistoryFor(_root));
        Assert.Equal(changeId, Assert.Single(controller.Monitor.GetRecentChanges()).Id);
    }

    [Fact]
    public void MonitoringDifferentRootReplacesRetainedHistory()
    {
        string target = Path.Combine(_root, "changed.txt");
        File.WriteAllText(target, "changed");
        using var controller = new QuickViewDirectorySizeController(() => { }, new ControlledCalculator());

        controller.Update(true, DirectoryItem(_root));
        controller.ToggleMonitor();
        controller.Monitor.RecordChange(DirectoryChangeKind.Changed, target, null);
        Assert.NotEmpty(controller.Monitor.GetRecentChanges());
        controller.ToggleMonitor();

        controller.Update(true, DirectoryItem(_otherRoot));
        controller.ToggleMonitor();

        Assert.True(controller.Monitor.IsEnabled);
        Assert.True(controller.Monitor.IsHistoryFor(_otherRoot));
        Assert.Empty(controller.Monitor.GetRecentChanges());
    }

    [Fact]
    public void ClosingQuickViewClearsRetainedMonitoringSession()
    {
        string target = Path.Combine(_root, "changed.txt");
        File.WriteAllText(target, "changed");
        using var controller = new QuickViewDirectorySizeController(() => { }, new ControlledCalculator());

        controller.Update(true, DirectoryItem(_root));
        controller.ToggleMonitor();
        controller.Monitor.RecordChange(DirectoryChangeKind.Changed, target, null);
        controller.ToggleMonitor();
        Assert.NotEmpty(controller.Monitor.GetRecentChanges());

        controller.Update(false, null);

        Assert.False(controller.Monitor.IsEnabled);
        Assert.Null(controller.Monitor.HistoryRoot);
        Assert.Empty(controller.Monitor.GetRecentChanges());
    }

    [Fact]
    public void RecentChangesOwnTheirHitRegion()
    {
        DirectoryChange[] changes = Enumerable.Range(1, 4)
            .Select(i => new DirectoryChange(i, DirectoryChangeKind.Changed, $"item-{i}.txt", null,
                Path.Combine(_root, $"item-{i}.txt"), DateTimeOffset.UnixEpoch.AddSeconds(i), i, 1))
            .ToArray();
        var list = new RoutedScrollableList<DirectoryChange>(
            new ScrollableListState<DirectoryChange>(changes),
            new UiTargetId("application.quick-view.recent-changes"),
            new UiTargetId("application.quick-view.recent-changes.scrollbar"),
            RoutedScrollableListOptions.DropdownPopup);
        ScrollableListFrame listFrame = list.CalculateFrame(new Rect(2, 6, 20, 2), new Rect(22, 6, 1, 3));
        var quickView = new ApplicationQuickViewFrame(
            new Rect(0, 0, 30, 20),
            new Rect(2, 4, 20, 1),
            [],
            changes[0].Id,
            changes.Select(change => change.Id).ToArray(),
            0,
            list,
            listFrame);
        var builder = new UiInteractionFrameBuilder();

        ApplicationUiSurface.AddQuickViewInteraction(builder, quickView, new ConsoleViewport(0, 0, 80, 25));
        UiInteractionFrame interaction = builder.Build();

        Assert.True(interaction.TryHitTest(3, 6, out UiHitRegion contentHit));
        Assert.Equal(list.ListTarget, contentHit.Target);
        Assert.True(interaction.TryHitTest(22, 6, out UiHitRegion scrollbarHit));
        Assert.Equal(list.ScrollbarTarget, scrollbarHit.Target);
    }

    private static FilePanelItem DirectoryItem(string path) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        IsDirectory = true,
    };

    private static FilePanelItem FileItem(string path) => new()
    {
        Name = Path.GetFileName(path),
        FullPath = path,
        IsDirectory = false,
    };

    private static MouseConsoleInputEvent Mouse(MouseButton button, int x, int y) =>
        new(x, y, button, MouseEventKind.Wheel, MouseKeyModifiers.None);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        if (Directory.Exists(_otherRoot)) Directory.Delete(_otherRoot, recursive: true);
    }

    private sealed class ControlledCalculator : IDirectorySizeCalculator
    {
        private long _nextOperationId;
        public event Action<DirectoryScanUpdate>? Progress;
        public event Action<DirectoryScanUpdate>? Completed;
        public int StartCount { get; private set; }

        public long Start(string path, DirectoryScanProgressMode progressMode, Action<long>? operationStarted = null)
        {
            StartCount++;
            long operationId = ++_nextOperationId;
            operationStarted?.Invoke(operationId);
            return operationId;
        }

        public void Cancel() { }
        public void Dispose() { }
    }
}
