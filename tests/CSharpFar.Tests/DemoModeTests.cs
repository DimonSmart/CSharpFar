using System.Text;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.Commands;
using CSharpFar.App.UserMenu;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class DemoModeTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "CSharpFar.DemoTests." + Guid.NewGuid().ToString("N"));

    public DemoModeTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void ApplicationRunOptionsParser_RequiresDemoPath()
    {
        bool parsed = ApplicationRunOptionsParser.TryParse(["--demo"], out _, out string? error);

        Assert.False(parsed);
        Assert.Contains("--demo <root-path>", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DemoFilePanelSource_UsesImportedSnapshotAfterPhysicalFixtureChanges()
    {
        string fixture = CreateFixture(("file.txt", "original"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);

        await File.WriteAllTextAsync(Path.Combine(fixture, "file.txt"), "changed outside");

        string text = await ReadAllTextAsync(source, "/file.txt");
        Assert.Equal("original", text);
    }

    [Fact]
    public async Task DemoFilePanelSource_OpenWrite_DoesNotModifyPhysicalFixture()
    {
        string fixture = CreateFixture(("file.txt", "original"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);

        await using (var stream = await source.OpenWriteAsync("/file.txt", overwrite: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes("edited in demo");
            await stream.WriteAsync(bytes);
        }

        Assert.Equal("edited in demo", await ReadAllTextAsync(source, "/file.txt"));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(fixture, "file.txt")));
    }

    [Fact]
    public async Task DemoFilePanelSource_Delete_DoesNotModifyPhysicalFixture()
    {
        string fixture = CreateFixture(("file.txt", "original"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);

        await source.DeleteAsync("/file.txt", recursive: false);

        Assert.Null(source.GetItem("/file.txt"));
        Assert.True(File.Exists(Path.Combine(fixture, "file.txt")));
    }

    [Fact]
    public async Task DemoFilePanelSource_CopyMoveRename_StayInMemory()
    {
        string fixture = CreateFixture(("file.txt", "original"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var registry = new FilePanelSourceRegistry([source]);
        var operations = new FileOperationService(registry);
        await source.CreateDirectoryAsync("/Copied");

        var copyResult = await operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/file.txt")],
                DestinationLocation = PanelLocation.Demo("/Copied"),
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver());

        Assert.Equal(1, copyResult.CopiedCount);
        Assert.Equal("original", await ReadAllTextAsync(source, "/Copied/file.txt"));

        var renameResult = await operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/Copied/file.txt")],
                DestinationLocation = PanelLocation.Demo("/renamed.txt"),
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver());

        Assert.Equal(1, renameResult.MovedCount);
        Assert.Null(source.GetItem("/Copied/file.txt"));
        Assert.Equal("original", await ReadAllTextAsync(source, "/renamed.txt"));
        Assert.False(File.Exists(Path.Combine(fixture, "renamed.txt")));
    }

    [Fact]
    public async Task DemoFilePanelSource_MoveIntoDirectory_StaysInMemory()
    {
        string fixture = CreateFixture(("A/file.txt", "original"), ("B/.keep", "keep"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var registry = new FilePanelSourceRegistry([source]);
        var operations = new FileOperationService(registry);

        var moveResult = await operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/A/file.txt")],
                DestinationLocation = PanelLocation.Demo("/B"),
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver());

        Assert.Equal(1, moveResult.MovedCount);
        Assert.Null(source.GetItem("/A/file.txt"));
        Assert.Equal("original", await ReadAllTextAsync(source, "/B/file.txt"));
        Assert.False(File.Exists(Path.Combine(fixture, "B", "file.txt")));
    }

    [Fact]
    public async Task DemoFilePanelSource_MoveMultipleItemsIntoDirectory_StaysInMemory()
    {
        string fixture = CreateFixture(("A/a.txt", "A"), ("A/b.txt", "B"), ("B/.keep", "keep"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var registry = new FilePanelSourceRegistry([source]);
        var operations = new FileOperationService(registry);

        var moveResult = await operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/A/a.txt"), PanelLocation.Demo("/A/b.txt")],
                DestinationLocation = PanelLocation.Demo("/B"),
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver());

        Assert.Equal(2, moveResult.MovedCount);
        Assert.Null(source.GetItem("/A/a.txt"));
        Assert.Null(source.GetItem("/A/b.txt"));
        Assert.Equal("A", await ReadAllTextAsync(source, "/B/a.txt"));
        Assert.Equal("B", await ReadAllTextAsync(source, "/B/b.txt"));
    }

    [Fact]
    public async Task DemoFilePanelSource_CopyToSameProviderItem_ThrowsWithoutChangingSource()
    {
        string fixture = CreateFixture(("A/file.txt", "original"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var operations = new FileOperationService(new FilePanelSourceRegistry([source]));

        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/A/file.txt")],
                DestinationLocation = PanelLocation.Demo("/A"),
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver()));

        Assert.Equal("original", await ReadAllTextAsync(source, "/A/file.txt"));
    }

    [Fact]
    public async Task DemoFilePanelSource_CopyDirectoryIntoDescendant_ThrowsBeforeMutation()
    {
        string fixture = CreateFixture(("A/B/file.txt", "value"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var operations = new FileOperationService(new FilePanelSourceRegistry([source]));

        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/A")],
                DestinationLocation = PanelLocation.Demo("/A/B"),
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver()));

        Assert.NotNull(source.GetItem("/A"));
        Assert.NotNull(source.GetItem("/A/B/file.txt"));
        Assert.Null(source.GetItem("/A/B/A"));
    }

    [Fact]
    public async Task DemoFilePanelSource_MoveDirectoryIntoDescendant_ThrowsBeforeMutation()
    {
        string fixture = CreateFixture(("A/B/file.txt", "value"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var operations = new FileOperationService(new FilePanelSourceRegistry([source]));

        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/A")],
                DestinationLocation = PanelLocation.Demo("/A/B"),
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver()));

        Assert.NotNull(source.GetItem("/A"));
        Assert.NotNull(source.GetItem("/A/B/file.txt"));
        Assert.Null(source.GetItem("/A/B/A"));
    }

    [Fact]
    public async Task DemoFilePanelSource_RenameAsync_RejectsDirectoryMoveIntoDescendant()
    {
        string fixture = CreateFixture(("A/B/file.txt", "value"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);

        await Assert.ThrowsAsync<IOException>(() => source.RenameAsync("/A", "/A/B/A"));

        Assert.NotNull(source.GetItem("/A"));
        Assert.NotNull(source.GetItem("/A/B/file.txt"));
        Assert.Null(source.GetItem("/A/B/A"));
    }

    [Fact]
    public async Task DemoFilePanelSource_MovePreflight_RejectsWholeRequestBeforeEarlierItemsMove()
    {
        string fixture = CreateFixture(("A/file.txt", "file"), ("A/Folder/child.txt", "child"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var operations = new FileOperationService(new FilePanelSourceRegistry([source]));

        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/A/file.txt"), PanelLocation.Demo("/A/Folder")],
                DestinationLocation = PanelLocation.Demo("/A/Folder/Subfolder"),
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver()));

        Assert.NotNull(source.GetItem("/A/file.txt"));
        Assert.NotNull(source.GetItem("/A/Folder"));
        Assert.NotNull(source.GetItem("/A/Folder/child.txt"));
        Assert.Null(source.GetItem("/A/Folder/Subfolder/file.txt"));
    }

    [Fact]
    public void DemoFilePanelSource_UsesVirtualRootSemantics()
    {
        string fixture = CreateFixture(("nested/file.txt", "value"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);

        Assert.True(source.IsRootPath("/"));
        Assert.Null(source.GetParentPath("/"));
        Assert.Equal("/", source.NormalizePath("/../../../../"));
        Assert.Equal("/nested", source.NormalizePath("/nested/child/.."));
    }

    [Fact]
    public void DemoCoreComposition_StartsPanelsOnDemoSourceAndDoesNotRegisterLocal()
    {
        string fixture = CreateFixture(("file.txt", "original"));
        var sourceRegistry = new FilePanelSourceRegistry([DemoFilePanelSource.ImportFromDirectory(fixture)]);
        string configDirectory = Path.Combine(_tempRoot, "config");
        Directory.CreateDirectory(configDirectory);

        CoreServices services = CoreServicesFactory.Create(
            new FileSystemService(),
            new InMemoryHistoryStore(),
            new AppSettings(),
            new UserMenuStore(configDirectory),
            volumeInfoService: null,
            mountPointService: null,
            fileLauncher: new DemoModeServices.DisabledFileLauncher(),
            searchService: new DemoModeServices.DisabledSearchService(),
            sourceRegistry: sourceRegistry,
            configDirectory: configDirectory,
            clipboard: null,
            runOptions: new ApplicationRunOptions(ApplicationRunMode.Demo, fixture));

        Assert.Equal(PanelSourceId.Demo, services.Session.Panels.Left.SourceId);
        Assert.Equal(PanelSourceId.Demo, services.Session.Panels.Right.SourceId);
        Assert.False(services.SourceRegistry.TryGetSource(PanelSourceId.Local, out _));
        Assert.IsType<DemoModeServices.DisabledSearchService>(services.SearchService);
        Assert.Throws<InvalidOperationException>(() => new DemoModeServices.DisabledShellService().Execute("dir", "/"));
        Assert.DoesNotContain(DemoModeServices.CreateVolumes(), volume => volume.RootPath != "/");
    }

    [Fact]
    public void DemoNavigateToRoot_StaysInsideDemoSource()
    {
        string fixture = CreateFixture(("Projects/SampleApp/file.txt", "value"));
        var context = CreateDemoCommandContext(fixture);
        context.Controller.LoadLocation(context.LeftPanel, PanelLocation.Demo("/Projects/SampleApp"), context.PanelOptions);

        new NavigateToRootCommand().Execute(
            context,
            new NavigateToRootArgs(PanelSide.Left, "/Projects/SampleApp"));

        Assert.Equal(PanelSourceId.Demo, context.LeftPanel.SourceId);
        Assert.Equal("/", context.LeftPanel.CurrentDirectory);
    }

    [Fact]
    public void DemoRegistry_RejectsLocalPanelLocationBeforeReadingPhysicalDirectory()
    {
        string fixture = CreateFixture(("file.txt", "value"));
        var fakeFs = new FakeFileSystemService();
        var sourceRegistry = new FilePanelSourceRegistry([DemoFilePanelSource.ImportFromDirectory(fixture)]);
        var builder = new PanelViewBuilder(fakeFs, new PanelSortService(), sources: sourceRegistry);
        var controller = new PanelController(builder);
        var state = new FilePanelState
        {
            CurrentLocation = PanelLocation.Demo("/"),
            ProviderCapabilities = PanelProviderCapabilities.Enumerate | PanelProviderCapabilities.OpenRead,
            CursorIndex = 1,
            ScrollOffset = 2,
        };
        state.Items.Add(new FilePanelItem
        {
            Name = "file.txt",
            FullPath = "/file.txt",
            SourceId = PanelSourceId.Demo,
            IsDirectory = false,
            Size = 5,
        });
        state.SelectedPaths.Add("/file.txt");
        state.SelectedLocations.Add(PanelLocation.Demo("/file.txt"));
        PanelLocation originalLocation = state.CurrentLocation;
        PanelSourceId originalSourceId = state.SourceId;
        PanelProviderCapabilities originalCapabilities = state.ProviderCapabilities;
        int originalCursor = state.CursorIndex;
        int originalScroll = state.ScrollOffset;
        string originalItemPath = state.Items[0].FullPath;
        PanelLocation originalItemLocation = state.Items[0].Location;

        bool loaded = controller.TryLoadLocation(
            state,
            PanelLocation.Local(Path.Combine(_tempRoot, "physical")),
            new AppSettings.PanelOptionsSettings());

        Assert.False(loaded);
        Assert.Equal(0, fakeFs.ReadDirectoryCallCount);
        Assert.Equal(originalSourceId, state.SourceId);
        Assert.Equal(originalLocation, state.CurrentLocation);
        Assert.Equal(originalCapabilities, state.ProviderCapabilities);
        Assert.Equal(originalCursor, state.CursorIndex);
        Assert.Equal(originalScroll, state.ScrollOffset);
        Assert.Null(state.LoadError);
        Assert.Single(state.Items);
        Assert.Equal(originalItemPath, state.Items[0].FullPath);
        Assert.Equal(originalItemLocation, state.Items[0].Location);
        Assert.Contains("/file.txt", state.SelectedPaths);
        Assert.Contains(PanelLocation.Demo("/file.txt"), state.SelectedLocations);
        Assert.DoesNotContain("physical", state.CurrentLocation.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoRegistry_RejectsAccidentalLocalDeleteBeforePhysicalIo()
    {
        string fixture = CreateFixture(("file.txt", "value"));
        string physicalFile = Path.Combine(_tempRoot, "physical.txt");
        await File.WriteAllTextAsync(physicalFile, "keep");

        var sourceRegistry = new FilePanelSourceRegistry([DemoFilePanelSource.ImportFromDirectory(fixture)]);
        var operations = new FileOperationService(
            sourceRegistry,
            new DemoModeServices.DisabledFileSystemPlatformOperations());

        await Assert.ThrowsAsync<IOException>(() => operations.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Delete,
                Sources = [physicalFile],
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new OverwriteConflictResolver()));

        Assert.True(File.Exists(physicalFile));
    }

    private string CreateFixture(params (string RelativePath, string Content)[] files)
    {
        string fixture = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        foreach ((string relativePath, string content) in files)
        {
            string fullPath = Path.Combine(fixture, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, Encoding.UTF8);
        }

        return fixture;
    }

    private static async Task<string> ReadAllTextAsync(DemoFilePanelSource source, string path)
    {
        await using var stream = await source.OpenReadAsync(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        return await reader.ReadToEndAsync();
    }

    private ApplicationCommandContext CreateDemoCommandContext(string fixture)
    {
        string configDirectory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDirectory);
        var settings = new AppSettings();
        var sourceRegistry = new FilePanelSourceRegistry([DemoFilePanelSource.ImportFromDirectory(fixture)]);
        var services = ApplicationServicesBuilder.Create(
            new ScreenRenderer(new FakeConsoleDriver()),
            new DemoModeServices.DisabledLocalFileSystemService(),
            new DemoModeServices.DisabledShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            new UserMenuStore(configDirectory),
            volumeService: new DemoModeServices.DemoVolumeService(),
            volumeInfoService: null,
            changeWatcher: null,
            locationService: null,
            mountPointService: null,
            fileLauncher: new DemoModeServices.DisabledFileLauncher(),
            searchService: new DemoModeServices.DisabledSearchService(),
            sourceRegistry: sourceRegistry,
            credentialStore: new DemoModeServices.EmptyCredentialStore(),
            enableBuiltInNetworkModules: false,
            configDirectory: configDirectory,
            runOptions: new ApplicationRunOptions(ApplicationRunMode.Demo, fixture));
        return services.CommandContext;
    }

    private sealed class OverwriteConflictResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }
}
