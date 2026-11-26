using CSharpFar.App;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.Files;
using CSharpFar.App.Panels;
using CSharpFar.App.Rendering;
using CSharpFar.App.State;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Menu;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class IsolatedApplicationCommandTests
{
    [Fact]
    public void ToggleSetting_RefreshesBothPanelsAndSavesWithoutApplication()
    {
        using var f = new PanelFixture();
        var refreshed = new List<FilePanelState>();
        f.Refresh.RefreshRequested = refreshed.Add;
        int saves = 0;
        var command = new ToggleShowHiddenAndSystemFilesCommand(f.Settings,
            new PanelSettingsChangeService(f.Refresh, f.Session.Panels, () => saves++));
        bool before = f.Settings.Panels.Options.ShowHiddenAndSystemFiles;

        Assert.True(command.CanExecute());
        Assert.True(command.Execute().ShouldRender);
        Assert.Equal(!before, f.Settings.Panels.Options.ShowHiddenAndSystemFiles);
        Assert.Equal(new[] { f.Left, f.Right }, refreshed);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void SortAndRefresh_UseLiveSideAndExplicitPanel()
    {
        using var f = new PanelFixture();
        var refreshed = new List<FilePanelState>();
        f.Refresh.RefreshRequested = refreshed.Add;
        var sort = new PanelSortServiceFacade(f.Controller, () => f.Settings.Panels.Options, _ => { })
        {
            Refresh = f.Refresh.SafeRefresh,
        };
        var command = new SortByNameCommand(f.Resolver, sort);
        f.Left.SortMode = SortMode.Size;
        f.Right.SortMode = SortMode.Extension;
        f.Workspace.SetActiveSide(PanelSide.Right);
        command.Execute();
        Assert.Equal(SortMode.Name, f.Right.SortMode);
        Assert.Equal(SortMode.Size, f.Left.SortMode);
        new PanelRefreshCommand(f.Workspace, f.Refresh).Execute(new PanelCommandArgs { PanelSide = PanelSide.Left });
        Assert.Equal(new[] { f.Right, f.Left }, refreshed);
    }

    [Fact]
    public void Copy_UsesLiveCapabilitiesAndCancelsWithoutInvokingProvider()
    {
        using var f = new PanelFixture();
        var command = new CopyCommand(f.Resolver, f.Controller, f.Dialogs, f.Fields, f.Registry, f.Operations, _ => { });
        Assert.True(command.CanExecute());
        f.Right.ProviderCapabilities = PanelProviderCapabilities.Enumerate;
        Assert.False(command.CanExecute());
        f.Right.ProviderCapabilities = PanelProviderCapabilities.LocalFileSystem;
        Assert.True(command.CanExecute());
        f.Left.SelectedLocations.Add(f.Left.Items[0].Location);
        f.Driver.EnqueueKey(Key(ConsoleKey.Escape));
        Assert.True(command.Execute().ShouldRender);
        Assert.Empty(f.FileOperations.Requests);
        Assert.Single(f.Left.SelectedLocations);
    }

    [Fact]
    public void Delete_RejectsReplacedCommittedItemAndExecutesCurrentItemOnNextInvocation()
    {
        using var f = new PanelFixture();
        f.Settings.Ui.ConfirmDelete = false;
        var command = new DeleteCommand(f.Resolver, f.Controller, f.Dialogs, f.Registry, f.Operations, f.Settings);
        var invocation = new ApplicationPanelCommandInvocation(PanelSide.Left, 10,
            ApplicationPanelKeyboardSnapshot.Capture(f.Left), ApplicationPanelKeyboardSnapshot.Capture(f.Right));
        f.Left.Items[0] = new FilePanelItem
        {
            Name = "replacement.txt", FullPath = Path.Combine(f.Root, "replacement.txt"), IsDirectory = false,
        };
        Assert.True(command.Execute(invocation).ShouldRender);
        Assert.Empty(f.FileOperations.Requests);
        command.Execute(new PanelCommandArgs { PanelSide = PanelSide.Left });
        Assert.Equal(f.Left.Items[0].Location.SourceId, Assert.Single(f.FileOperations.Requests).Sources[0].SourceId);
        Assert.Equal(Path.Combine(f.Root, "replacement.txt"), f.FileOperations.Requests[0].Sources[0].SourcePath);
    }

    [Fact]
    public void View_OpensCurrentFileAndAddsHistoryWithoutApplication()
    {
        using var f = new PanelFixture();
        var viewer = new PanelFileViewerService(f.Host.Surfaces, f.Host.ModalDialogs, f.Dialogs,
            () => f.Session.App.Palette, f.Registry, f.History, new MemoryClipboard(), f.Fields, f.Settings,
            f.Controller, f.Workspace.PanelSideForState, f.Workspace.VisibleRows, f.Refresh.SafeRefresh);
        var command = new ViewFileCommand(f.Resolver, f.Controller, viewer);
        Assert.True(command.CanExecute());
        f.Left.ProviderCapabilities = PanelProviderCapabilities.Enumerate;
        Assert.False(command.CanExecute());
        f.Left.ProviderCapabilities = PanelProviderCapabilities.LocalFileSystem;
        f.Driver.EnqueueKey(Key(ConsoleKey.F10));
        Assert.True(command.Execute().ShouldRender);
        Assert.Equal(Path.Combine(f.Root, "a.txt"), Assert.Single(f.History.GetFileHistory()).Path);
    }

    [Fact]
    public void Search_UsesTargetProviderAndResetsFunctionKeysOnCancellation()
    {
        using var f = new PanelFixture();
        var searchResults = new PanelSearchResultsService(f.Dialogs, f.Controller, f.History,
            () => f.Settings.Panels.Options, f.Workspace.PanelSideForState, f.Workspace.VisibleRows,
            _ => { }, _ => { }, (_, _) => { });
        int resets = 0;
        var command = new SearchFilesCommand(f.Resolver, f.Registry, f.Dialogs, f.Fields,
            new FileSystemSearchService(), searchResults, () => resets++);
        f.Right.CurrentLocation = new PanelLocation(new PanelSourceId("nonlocal"), "/");
        Assert.True(command.CanExecute());
        Assert.False(command.CanExecute(new PanelCommandArgs { PanelSide = PanelSide.Right }));
        f.Driver.EnqueueKey(Key(ConsoleKey.Escape));
        Assert.True(command.Execute().ShouldRender);
        Assert.Equal(1, resets);
        Assert.Null(f.Left.SearchRequest);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private sealed class PanelFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"CSharpFarIsolatedCommands_{Guid.NewGuid():N}");
        public FakeConsoleDriver Driver { get; } = new(width: 100, height: 30);
        public UiTestHost Host { get; }
        public FormFieldFactory Fields { get; } = new();
        public DialogService Dialogs { get; }
        public AppSettings Settings { get; } = new();
        public InMemoryHistoryStore History { get; } = new();
        public PanelController Controller { get; }
        public FilePanelSourceRegistry Registry { get; }
        public ApplicationSession Session { get; }
        public FilePanelState Left => Session.Panels.Left;
        public FilePanelState Right => Session.Panels.Right;
        public PanelWorkspaceController Workspace { get; }
        public PanelCommandTargetResolver Resolver { get; }
        public PanelRefreshService Refresh { get; }
        public RecordingFileOperations FileOperations { get; } = new();
        public PanelFileOperationWorkflow Operations { get; }

        public PanelFixture()
        {
            Directory.CreateDirectory(Root);
            string file = Path.Combine(Root, "a.txt");
            File.WriteAllText(file, "isolated command test");
            string other = Path.Combine(Root, "other");
            Directory.CreateDirectory(other);
            var fs = new FakeFileSystemService();
            fs.AddDirectory(Root, new FilePanelItem { Name = "a.txt", FullPath = file, IsDirectory = false });
            fs.AddDirectory(other);
            Settings.Panels.LeftStartDirectory = Root;
            Settings.Panels.RightStartDirectory = other;
            Controller = new PanelController(new FakePanelViewBuilder(fs));
            Registry = new FilePanelSourceRegistry([new LocalFilePanelSource(fs)]);
            Session = ApplicationSessionFactory.Create(Settings, Controller, fs);
            Host = UiTestHost.Create(Driver);
            Dialogs = new DialogService(Host.ModalDialogs, Fields);
            var quickSearch = new PanelQuickSearchController(Controller,
                () => Session.Panels.ActiveSide, () => true,
                side => side == PanelSide.Left ? Left : Right, _ => 10);
            Workspace = new PanelWorkspaceController(Host.Screen, Session, quickSearch, () => Settings.Panels.Options);
            Resolver = new PanelCommandTargetResolver(Workspace);
            Refresh = new PanelRefreshService(Controller, () => Settings.Panels.Options, Workspace.VisibleRows, quickSearch.CloseForState);
            var runner = new FileOperationUiRunner(Host.ModalDialogs, Dialogs, () => Session.App.Palette,
                FileOperations, () => false, Fields);
            Operations = new PanelFileOperationWorkflow(runner, FileOperations, Dialogs, Settings, Refresh, Session.Panels);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class RecordingFileOperations : IFileOperationService
    {
        public bool SupportsRecycleBin => false;
        public List<FileOperationRequest> Requests { get; } = [];
        public Task<FileOperationResult> ExecuteAsync(FileOperationRequest request, IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
        }
    }

    private sealed class MemoryClipboard : ITextClipboard
    {
        private string _text = "";
        public bool TrySetText(string text) { _text = text; return true; }
        public bool TryGetText(out string text) { text = _text; return true; }
    }
}
