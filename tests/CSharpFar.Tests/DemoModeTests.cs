using System.Text;
using CSharpFar.App.Bootstrap;
using CSharpFar.App.UserMenu;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

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
            searchService: new FileSystemSearchService(),
            sourceRegistry: sourceRegistry,
            configDirectory: configDirectory,
            clipboard: null,
            runOptions: new ApplicationRunOptions(ApplicationRunMode.Demo, fixture));

        Assert.Equal(PanelSourceId.Demo, services.Session.Panels.Left.SourceId);
        Assert.Equal(PanelSourceId.Demo, services.Session.Panels.Right.SourceId);
        Assert.False(services.SourceRegistry.TryGetSource(PanelSourceId.Local, out _));
        Assert.Throws<InvalidOperationException>(() => new DemoModeServices.DisabledShellService().Execute("dir", "/"));
        Assert.DoesNotContain(DemoModeServices.CreateVolumes(), volume => volume.RootPath != "/");
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

    private sealed class OverwriteConflictResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }
}
