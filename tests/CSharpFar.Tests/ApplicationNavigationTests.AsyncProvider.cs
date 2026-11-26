using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

public sealed partial class ApplicationNavigationTests
{
    [Fact]
    public void SlowProviderDoesNotBlockOtherPanelResizeOrLogicalPanelSwap()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_tempDir,
            new FilePanelItem { Name = "first.txt", FullPath = Path.Combine(_tempDir, "first.txt"), IsDirectory = false },
            new FilePanelItem { Name = "second.txt", FullPath = Path.Combine(_tempDir, "second.txt"), IsDirectory = false });
        var source = new DelayedNavigationSource();
        var registry = new FilePanelSourceRegistry([new LocalFilePanelSource(fs), source]);
        var settings = new AppSettings();
        settings.Application.ConfirmExit = false;
        settings.Panels.LeftStartDirectory = _tempDir;
        settings.Panels.RightStartDirectory = _tempDir;
        var driver = new FakeConsoleDriver(80, 25);
        var app = new Application(new ScreenRenderer(driver), fs, new NoOpShellService(),
            new NoOpFileOperationService(), new InMemoryHistoryStore(), settings, sourceRegistry: registry);
        var slowPanel = GetLeftPanel(app);
        var otherPanel = GetRightPanel(app);
        var initial = slowPanel.CurrentLocation;
        try
        {
            app.CommandContext.NavigatePanel(slowPanel, new PanelLocation(source.SourceId, "/slow"), true, _ => { });
            Assert.True(source.Started.Task.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotEqual(Environment.CurrentManagedThreadId, source.WorkerThread);
            Assert.True(SpinWait.SpinUntil(() =>
            {
                app.CommandContext.DrainUiActions?.Invoke();
                return slowPanel.IsLoadingIndicatorVisible;
            }, TimeSpan.FromSeconds(10)));
            Render(app);
            Assert.Contains("Loading", string.Join(Environment.NewLine,
                Enumerable.Range(0, driver.GetSize().Height).Select(driver.GetRow)));
            Assert.Equal(initial, slowPanel.CurrentLocation);

            HandleKeyAndRender(app, Key(ConsoleKey.Tab));
            Assert.Equal(PanelSide.Right, app.CommandContext.ActiveSide);
            int cursor = otherPanel.CursorIndex;
            HandleKeyAndRender(app, Key(ConsoleKey.DownArrow));
            Assert.True(otherPanel.CursorIndex > cursor);
            Assert.True(slowPanel.IsLoading);

            driver.SetSize(100, 20);
            Render(app);
            Assert.Contains("Loading", string.Join(Environment.NewLine,
                Enumerable.Range(0, driver.GetSize().Height).Select(driver.GetRow)));
            app.CommandContext.SwapPanels();
            Render(app);
            Assert.Same(slowPanel, GetRightPanel(app));
            Assert.Same(otherPanel, GetLeftPanel(app));

            source.Completion.TrySetResult([new FilePanelItem
            {
                Name = "remote.txt", FullPath = "/slow/remote.txt", SourceId = source.SourceId, IsDirectory = false,
            }]);
            app.WaitForPanelLoads();
            Assert.Equal(new PanelLocation(source.SourceId, "/slow"), GetRightPanel(app).CurrentLocation);
            Assert.Contains(GetRightPanel(app).Items, item => item.Name == "remote.txt");
            Assert.Equal(initial, GetLeftPanel(app).CurrentLocation);
            Assert.False(slowPanel.IsLoading);
            Assert.False(slowPanel.IsLoadingIndicatorVisible);
        }
        finally
        {
            source.Completion.TrySetResult([]);
        }
    }

    private sealed class DelayedNavigationSource() : MemoryFilePanelView(new PanelSourceId("delayed-navigation")), IAsyncFilePanelSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<IReadOnlyList<FilePanelItem>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WorkerThread { get; private set; }
        public Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(string path, CancellationToken token = default)
        {
            WorkerThread = Environment.CurrentManagedThreadId;
            Started.TrySetResult();
            return Completion.Task;
        }

        public Task<FilePanelItem?> GetItemAsync(string path, CancellationToken token = default) =>
            Task.FromResult(GetItem(path, token));
    }
}
