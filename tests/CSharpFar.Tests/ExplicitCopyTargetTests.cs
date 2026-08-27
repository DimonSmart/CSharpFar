using CSharpFar.App.Bootstrap;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class ExplicitCopyTargetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarExplicitCopy_{Guid.NewGuid():N}");

    public ExplicitCopyTargetTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task LocalCopy_SingleFileToFullPath_UsesExactTargetName()
    {
        string sourceDirectory = Path.Combine(_root, "source");
        string destinationDirectory = Path.Combine(_root, "destination");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);
        string source = Path.Combine(sourceDirectory, "source.txt");
        string target = Path.Combine(destinationDirectory, "renamed.txt");
        await File.WriteAllTextAsync(source, "new");

        var service = new FileOperationService();
        FileOperationResult result = await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [source],
                Destination = target,
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            },
            progress: null,
            conflictResolver: new RecordingConflictResolver(ConflictDecisionMode.Overwrite));

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.False(Directory.Exists(target));
        Assert.False(File.Exists(Path.Combine(target, "source.txt")));
    }

    [Fact]
    public async Task LocalCopy_SingleFileToExistingFullPath_UsesConflictResolverForExactTarget()
    {
        string sourceDirectory = Path.Combine(_root, "source-conflict");
        string destinationDirectory = Path.Combine(_root, "destination-conflict");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);
        string source = Path.Combine(sourceDirectory, "source.txt");
        string target = Path.Combine(destinationDirectory, "renamed.txt");
        await File.WriteAllTextAsync(source, "new");
        await File.WriteAllTextAsync(target, "old");
        var resolver = new RecordingConflictResolver(ConflictDecisionMode.Skip);

        var service = new FileOperationService();
        FileOperationResult result = await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [source],
                Destination = target,
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Ask,
                },
            },
            progress: null,
            conflictResolver: resolver);

        Assert.Equal(1, result.SkippedCount);
        Assert.NotNull(resolver.LastConflict);
        Assert.Equal(target, resolver.LastConflict!.DestinationPath);
        Assert.Equal("old", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task ProviderCopy_SingleFileToFullPath_UsesExactTargetName()
    {
        string fixture = CreateFixture(("source.txt", "new"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var service = new FileOperationService(new FilePanelSourceRegistry([source]));

        FileOperationResult result = await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/source.txt")],
                DestinationLocation = PanelLocation.Demo("/renamed.txt"),
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            },
            progress: null,
            conflictResolver: new RecordingConflictResolver(ConflictDecisionMode.Overwrite));

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("new", await ReadAllTextAsync(source, "/renamed.txt"));
        Assert.False(source.GetItem("/renamed.txt")!.IsDirectory);
        Assert.Null(source.GetItem("/renamed.txt/source.txt"));
    }

    [Fact]
    public async Task ProviderCopy_SingleFileToExistingFullPath_UsesConflictResolverForExactTarget()
    {
        string fixture = CreateFixture(("source.txt", "new"), ("renamed.txt", "old"));
        var source = DemoFilePanelSource.ImportFromDirectory(fixture);
        var service = new FileOperationService(new FilePanelSourceRegistry([source]));
        var resolver = new RecordingConflictResolver(ConflictDecisionMode.Skip);

        FileOperationResult result = await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [PanelLocation.Demo("/source.txt")],
                DestinationLocation = PanelLocation.Demo("/renamed.txt"),
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Ask,
                },
            },
            progress: null,
            conflictResolver: resolver);

        Assert.Equal(1, result.SkippedCount);
        Assert.NotNull(resolver.LastConflict);
        Assert.Equal("/renamed.txt", resolver.LastConflict!.DestinationPath);
        Assert.Equal("old", await ReadAllTextAsync(source, "/renamed.txt"));
    }

    private string CreateFixture(params (string Path, string Content)[] files)
    {
        string fixture = Path.Combine(_root, $"fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixture);
        foreach ((string relativePath, string content) in files)
        {
            string path = Path.Combine(fixture, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return fixture;
    }

    private static async Task<string> ReadAllTextAsync(DemoFilePanelSource source, string path)
    {
        await using Stream stream = await source.OpenReadAsync(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class RecordingConflictResolver : IFileOperationConflictResolver
    {
        private readonly ConflictDecisionMode _decision;

        public RecordingConflictResolver(ConflictDecisionMode decision)
        {
            _decision = decision;
        }

        public FileOperationConflict? LastConflict { get; private set; }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            LastConflict = conflict;
            return FileOperationConflictDecision.FromMode(_decision);
        }
    }
}
