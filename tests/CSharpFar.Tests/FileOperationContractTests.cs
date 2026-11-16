using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Infrastructure;

namespace CSharpFar.Tests;

/// <summary>Identical user-level contracts for local, virtual and transactional providers.</summary>
public sealed class FileOperationContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"csharpfar-transfer-contract-{Guid.NewGuid():N}");

    public FileOperationContractTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("demo")]
    [InlineData("batch")]
    public async Task Copy_PreviewMatchesExecutedTargets(string backend)
    {
        ContractFixture fixture = Arrange(backend,
            ("src/a.txt", "alpha"),
            ("src/b.txt", "beta"),
            ("dest/keep.txt", "keep"));
        FileOperationRequest request = fixture.Request(
            FileOperationKind.Copy, ["src/a.txt", "src/b.txt"], "dest");

        FileOperationPlan plan = fixture.Service.BuildPlan(request);
        Assert.Equal(
            [fixture.Path("dest/a.txt"), fixture.Path("dest/b.txt")],
            plan.Items.Select(item => item.Destination.SourcePath).ToArray());
        Assert.All(plan.Items, item => Assert.Equal(
            fixture.SourceId, item.Destination.SourceId));

        FileOperationResult result = await fixture.Service.ExecuteAsync(
            request, progress: null, conflictResolver: new ScriptedResolver());

        Assert.Equal(2, result.CopiedCount);
        Assert.Equal("alpha", await fixture.ReadAsync("dest/a.txt"));
        Assert.Equal("beta", await fixture.ReadAsync("dest/b.txt"));
        Assert.Equal("keep", await fixture.ReadAsync("dest/keep.txt"));
    }

    [Theory]
    [InlineData("local", FileOperationKind.Copy)]
    [InlineData("demo", FileOperationKind.Copy)]
    [InlineData("batch", FileOperationKind.Copy)]
    [InlineData("local", FileOperationKind.Move)]
    [InlineData("demo", FileOperationKind.Move)]
    [InlineData("batch", FileOperationKind.Move)]
    public async Task SkipAll_OnlyAsksOnceAndPreservesBothTrees(
        string backend, FileOperationKind kind)
    {
        ContractFixture fixture = Arrange(backend,
            ("src/a.txt", "new-a"), ("src/b.txt", "new-b"),
            ("dest/a.txt", "old-a"), ("dest/b.txt", "old-b"));
        var resolver = new ScriptedResolver(
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.SkipAll));

        FileOperationResult result = await fixture.Service.ExecuteAsync(
            fixture.Request(kind, ["src/a.txt", "src/b.txt"], "dest"),
            progress: null, conflictResolver: resolver);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Equal("old-a", await fixture.ReadAsync("dest/a.txt"));
        Assert.Equal("old-b", await fixture.ReadAsync("dest/b.txt"));
        Assert.Equal("new-a", await fixture.ReadAsync("src/a.txt"));
        Assert.Equal("new-b", await fixture.ReadAsync("src/b.txt"));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("demo")]
    [InlineData("batch")]
    public async Task OverwriteAll_ReusesDecisionAcrossFiles(string backend)
    {
        ContractFixture fixture = Arrange(backend,
            ("src/a.txt", "new-a"), ("src/b.txt", "new-b"),
            ("dest/a.txt", "old-a"), ("dest/b.txt", "old-b"));
        var resolver = new ScriptedResolver(
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.OverwriteAll));

        FileOperationResult result = await fixture.Service.ExecuteAsync(
            fixture.Request(FileOperationKind.Copy, ["src/a.txt", "src/b.txt"], "dest"),
            progress: null, conflictResolver: resolver);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(2, result.CopiedCount);
        Assert.Equal("new-a", await fixture.ReadAsync("dest/a.txt"));
        Assert.Equal("new-b", await fixture.ReadAsync("dest/b.txt"));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("demo")]
    [InlineData("batch")]
    public async Task RenameToOccupiedTarget_RechecksConflictBeforeWriting(string backend)
    {
        ContractFixture fixture = Arrange(backend,
            ("src/a.txt", "new"), ("dest/a.txt", "old"),
            ("dest/protected.txt", "protected"));
        var resolver = new ScriptedResolver(
            new FileOperationConflictDecision
            {
                Mode = ConflictDecisionMode.Rename,
                NewDestinationPath = fixture.Path("dest/protected.txt"),
            },
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Skip));

        FileOperationResult result = await fixture.Service.ExecuteAsync(
            fixture.Request(FileOperationKind.Copy, ["src/a.txt"], "dest"),
            progress: null, conflictResolver: resolver);

        Assert.Equal(2, resolver.CallCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("old", await fixture.ReadAsync("dest/a.txt"));
        Assert.Equal("protected", await fixture.ReadAsync("dest/protected.txt"));
        Assert.Equal("new", await fixture.ReadAsync("src/a.txt"));
    }

    [Theory]
    [InlineData("local")]
    [InlineData("demo")]
    [InlineData("batch")]
    public void TemplateCollisions_AreRejectedDuringPlanning(string backend)
    {
        ContractFixture fixture = Arrange(backend,
            ("src/a.txt", "one"), ("other/a.txt", "two"),
            ("dest/keep.txt", "keep"));
        FileOperationRequest request = fixture.Request(
            FileOperationKind.Copy, ["src/a.txt", "other/a.txt"],
            "dest/{name}{ext}", useDestinationTemplate: true);

        Assert.Throws<IOException>(() => fixture.Service.BuildPlan(request));
    }

    private ContractFixture Arrange(string backend, params (string Path, string Text)[] files)
    {
        foreach ((string path, string text) in files)
        {
            string absolutePath = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
            File.WriteAllText(absolutePath, text);
        }

        IFilePanelSource? provider = backend switch
        {
            "local" => null,
            "demo" => DemoFilePanelSource.ImportFromDirectory(_root),
            "batch" => MakeBatch(files),
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };
        return new ContractFixture(_root, provider);
    }

    private static InMemoryBatchFilePanelSource MakeBatch(IEnumerable<(string Path, string Text)> files)
    {
        var batch = new InMemoryBatchFilePanelSource();
        batch.SeedDirectory("/src");
        batch.SeedDirectory("/dest");
        batch.SeedDirectory("/other");
        foreach ((string path, string text) in files)
            batch.SeedFile("/" + path, text);
        return batch;
    }

    private sealed class ContractFixture(string root, IFilePanelSource? provider)
    {
        public FileOperationService Service { get; } = provider is null
            ? new FileOperationService()
            : new FileOperationService(new FilePanelSourceRegistry([provider]));

        public PanelSourceId SourceId => provider?.SourceId ?? PanelSourceId.Local;

        public string Path(string relative) => provider is null
            ? System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar))
            : "/" + relative;

        public FileOperationRequest Request(
            FileOperationKind kind,
            string[] sources,
            string destination,
            bool useDestinationTemplate = false) => new()
        {
            Kind = kind,
            Sources = provider is null ? sources.Select(Path).ToArray() : [],
            SourceLocations = provider is null
                ? null
                : sources.Select(path => new PanelLocation(provider.SourceId, Path(path))).ToArray(),
            Destination = provider is null ? Path(destination) : null,
            DestinationLocation = provider is null
                ? null
                : new PanelLocation(provider.SourceId, Path(destination)),
            UseDestinationTemplate = useDestinationTemplate,
            Options = new FileOperationOptions(),
        };

        public async Task<string> ReadAsync(string relative)
        {
            if (provider is null)
                return await File.ReadAllTextAsync(Path(relative));

            await using Stream stream = await provider.OpenReadAsync(Path(relative));
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
    }

    private sealed class ScriptedResolver(params FileOperationConflictDecision[] decisions)
        : IFileOperationConflictResolver
    {
        public int CallCount { get; private set; }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            if (CallCount >= decisions.Length)
                throw new InvalidOperationException("Unexpected conflict prompt.");
            return decisions[CallCount++];
        }
    }
}
