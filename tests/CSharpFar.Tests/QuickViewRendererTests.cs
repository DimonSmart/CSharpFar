using CSharpFar.App.Input;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 16: QuickViewRenderer draws the correct content for
/// null items, directories, and text files.
/// </summary>
public class QuickViewRendererTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeConsoleDriver _driver;
    private readonly ScreenRenderer _screen;
    private readonly Rect _bounds = new(0, 0, 40, 10);

    public QuickViewRendererTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarQVTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _driver = new FakeConsoleDriver(80, 25);
        _screen = new ScreenRenderer(_driver);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string ContentRow(int row) =>
        _driver.GetRegionText(new Rect(_bounds.X + 1, _bounds.Y + 1 + row, _bounds.Width - 2, 1));

    [Theory]
    [MemberData(nameof(NoSelectionItems))]
    public void NoPreviewableItem_ShowsNoFileSelected(FilePanelItem? item)
    {
        Render(item);

        Assert.Contains("No file selected", ContentRow(0));
    }

    [Fact]
    public void DirectoryItem_ShowsPathAndDirectCounts()
    {
        string subDir = Path.Combine(_tempDir, "testDir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(subDir, "b.txt"), "world");

        var item = new FilePanelItem { Name = "testDir", FullPath = subDir, IsDirectory = true };
        Render(item);

        string row0 = ContentRow(0);
        Assert.Contains("testDir", row0);
        Assert.Contains("Files:", ContentRow(6));
        Assert.Contains("2", ContentRow(6));
        Assert.Contains("Directories:", ContentRow(7));
        Assert.Contains("0", ContentRow(7));
    }

    [Fact]
    public void BackgroundRefresh_RetainsCompletedSizeAndShowsUpdatingIndicator()
    {
        var item = new FilePanelItem { Name = "directory", FullPath = _tempDir, IsDirectory = true };

        UiTestRender.Render(_screen, canvas =>
            new QuickViewRenderer(canvas).Render(
                new Rect(0, 0, 40, 16),
                item,
                new DirectorySizeState(100, true, []),
                isBackgroundUpdating: true));

        Assert.Contains("100 bytes", ContentRow(9));
        Assert.Contains("updating", ContentRow(9));
    }

    [Fact]
    public void FileItem_ShowsTextContent()
    {
        string filePath = Path.Combine(_tempDir, "preview.txt");
        File.WriteAllText(filePath, "line one\nline two\nline three");

        var item = new FilePanelItem { Name = "preview.txt", FullPath = filePath, IsDirectory = false };
        Render(item);

        Assert.Contains("line one", ContentRow(0));
        Assert.Contains("line two", ContentRow(1));
        Assert.Contains("line three", ContentRow(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Render_NarrowBounds_DoesNotThrow(int width)
    {
        string filePath = Path.Combine(_tempDir, "narrow.txt");
        File.WriteAllText(filePath, "line one");

        var directoryItem = new FilePanelItem { Name = "dir", FullPath = _tempDir, IsDirectory = true };
        var fileItem = new FilePanelItem { Name = "narrow.txt", FullPath = filePath, IsDirectory = false };
        var bounds = new Rect(0, 0, width, 5);

        Render(null, bounds);
        Render(directoryItem, bounds);
        Render(fileItem, bounds);
    }

    [Fact]
    public void DirectoryMonitor_UsesAvailableHeightForVisibleChanges()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        monitor.Enable(_tempDir);
        for (int i = 0; i < 20; i++)
            monitor.RecordChange(DirectoryChangeKind.Created, Path.Combine(_tempDir, $"item-{i}.txt"), null);
        var item = new FilePanelItem { Name = "directory", FullPath = _tempDir, IsDirectory = true };
        var tallDriver = new FakeConsoleDriver(80, 50);
        var tallScreen = new ScreenRenderer(tallDriver);
        ApplicationQuickViewFrame? tallFrame = null;

        UiTestRender.Render(tallScreen, canvas =>
            tallFrame = new QuickViewRenderer(canvas).Render(new Rect(0, 0, 40, 40), item, monitor: monitor));

        Assert.NotNull(tallFrame);
        Assert.True(tallFrame.ChangeHits.Count > 10);
        Assert.All(tallFrame.ChangeHits, hit => Assert.InRange(hit.Bounds.Bottom, 1, tallFrame.Bounds.Bottom - 1));

        ApplicationQuickViewFrame? smallFrame = null;
        UiTestRender.Render(_screen, canvas =>
            smallFrame = new QuickViewRenderer(canvas).Render(_bounds, item, monitor: monitor));

        Assert.NotNull(smallFrame);
        Assert.True(smallFrame.ChangeHits.Count < tallFrame.ChangeHits.Count);
        Assert.Equal(20, monitor.GetRecentChanges().Count);
    }

    [Fact]
    public void VisibleMonitorSelection_IsNormalizedAfterResize()
    {
        using var controller = new QuickViewDirectorySizeController(() => { });
        controller.SetVisibleMonitorChanges([1, 2, 3]);
        Assert.True(controller.SelectMonitorChange(3));

        controller.SetVisibleMonitorChanges([1]);

        Assert.Equal(1, controller.SelectedMonitorChangeId);
        Assert.False(controller.SelectMonitorChange(2));
    }

    [Fact]
    public void Render_NormalizesSelectionBeforeDrawingVisibleChanges()
    {
        using var monitor = new DirectorySummaryMonitor(() => { }, _ => { });
        using var controller = new QuickViewDirectorySizeController(() => { });
        monitor.Enable(_tempDir);
        for (int i = 1; i <= 3; i++)
            monitor.RecordChange(DirectoryChangeKind.Created, Path.Combine(_tempDir, $"item-{i}.txt"), null);

        DirectoryChange[] changes = monitor.GetRecentChanges().ToArray();
        controller.SetVisibleMonitorChanges(changes.Select(change => change.Id).ToArray());
        Assert.True(controller.SelectMonitorChange(changes[^1].Id));
        var item = new FilePanelItem { Name = "directory", FullPath = _tempDir, IsDirectory = true };
        ApplicationQuickViewFrame? frame = null;

        UiTestRender.Render(_screen, canvas =>
            frame = new QuickViewRenderer(canvas).Render(
                new Rect(0, 0, 40, 16),
                item,
                monitor: monitor,
                selectedChangeId: controller.SelectedMonitorChangeId,
                normalizeSelection: controller.NormalizeVisibleMonitorChanges));

        Assert.NotNull(frame);
        Assert.Single(frame.ChangeHits);
        Assert.Equal(frame.ChangeHits[0].ChangeId, controller.SelectedMonitorChangeId);
        Assert.Equal('>', _driver.GetRegionText(new Rect(frame.ChangeHits[0].Bounds.X, frame.ChangeHits[0].Bounds.Y, 1, 1))[0]);
    }

    [Fact]
    public void MonitorTogglePointer_UsesTheSharedToggleOperation()
    {
        int toggles = 0;
        var context = new MouseInputContext
        {
            PanelController = new PanelController(new FakePanelViewBuilder(new FakeFileSystemService())),
            CommandLine = new CommandLineState(),
            Ui = new UiTransientState(),
            Mouse = new MouseSessionState(),
            PanelOptions = () => new AppSettings.PanelOptionsSettings(),
            SetActiveSide = _ => { },
            GetPanelState = _ => new FilePanelState(),
            ToggleQuickViewDirectoryMonitor = () => { toggles++; return true; },
        };

        ApplicationInputHandlingResult result = new ApplicationQuickViewInputHandler(context)
            .Handle(new ApplicationQuickViewPointerInteraction(new ApplicationQuickViewMonitorToggleTarget()));

        Assert.True(result.Handled);
        Assert.Equal(1, toggles);
    }

    public static TheoryData<FilePanelItem?> NoSelectionItems() => new()
    {
        null,
        new FilePanelItem { Name = "..", FullPath = @"C:\", IsDirectory = true, IsParentDirectory = true },
    };

    private void Render(FilePanelItem? item, Rect? bounds = null)
    {
        UiTestRender.Render(_screen, canvas =>
        {
            var renderer = new QuickViewRenderer(canvas);
            renderer.Render(bounds ?? _bounds, item);
        });
    }
}
