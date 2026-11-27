using CSharpFar.Console;
using CSharpFar.App.Commands;
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
            int uiThread = Environment.CurrentManagedThreadId;
            app.PanelNavigation.Navigate(slowPanel, new PanelLocation(source.SourceId, "/slow"), true, _ => { });
            // Drive the UI queue on the original thread throughout the scenario.
            Assert.True(SpinWait.SpinUntil(() =>
            {
                app.DrainUiActions();
                return source.WorkerThread != 0 && slowPanel.IsLoadingIndicatorVisible;
            }, TimeSpan.FromSeconds(10)));
            Assert.NotEqual(uiThread, source.WorkerThread);
            Render(app);
            Assert.Contains("Loading", string.Join(Environment.NewLine,
                Enumerable.Range(0, driver.GetSize().Height).Select(driver.GetRow)));
            Assert.Equal(initial, slowPanel.CurrentLocation);

            HandleKeyAndRender(app, Key(ConsoleKey.Tab));
            Assert.Equal(PanelSide.Right, app.Session.Panels.ActiveSide);
            int cursor = otherPanel.CursorIndex;
            HandleKeyAndRender(app, Key(ConsoleKey.DownArrow));
            Assert.True(otherPanel.CursorIndex > cursor);
            Assert.True(slowPanel.IsLoading);

            driver.SetSize(100, 20);
            Render(app);
            Assert.Contains("Loading", string.Join(Environment.NewLine,
                Enumerable.Range(0, driver.GetSize().Height).Select(driver.GetRow)));
            app.CommandRegistry.Execute(ApplicationCommandIds.SwapPanels);
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
        private int _workerThread;
        public TaskCompletionSource<IReadOnlyList<FilePanelItem>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WorkerThread => Volatile.Read(ref _workerThread);
        public Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(string path, CancellationToken token = default)
        {
            Volatile.Write(ref _workerThread, Environment.CurrentManagedThreadId);
            return Completion.Task;
        }

        public Task<FilePanelItem?> GetItemAsync(string path, CancellationToken token = default) =>
            Task.FromResult(GetItem(path, token));
    }
}
