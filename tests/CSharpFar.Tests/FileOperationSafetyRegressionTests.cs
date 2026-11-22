using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class FileOperationSafetyRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"csharpfar-file-operation-safety-{Guid.NewGuid():N}");

    public FileOperationSafetyRegressionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ProviderCopy_RenameToOccupiedPath_AsksAgainWithoutOverwriting()
    {
        DemoFilePanelSource provider = Demo(
            ("source.txt", "new"),
            ("target.txt", "original"),
            ("occupied.txt", "protected"));
        var resolver = new SequenceResolver(
            new FileOperationConflictDecision
            {
                Mode = ConflictDecisionMode.Rename,
                NewDestinationPath = "/occupied.txt",
            },
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Skip));

        FileOperationResult result = await ExecuteProvider(
            provider, FileOperationKind.Copy, ["/source.txt"], "/target.txt", resolver);

        Assert.Equal(2, resolver.CallCount);
        Assert.Equal(new[] { "/target.txt", "/occupied.txt" }, resolver.Conflicts.Select(c => c.DestinationPath));
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal("original", await ReadDemo(provider, "/target.txt"));
        Assert.Equal("protected", await ReadDemo(provider, "/occupied.txt"));
        Assert.Equal("new", await ReadDemo(provider, "/source.txt"));
    }

    [Fact]
    public async Task ProviderCopy_RenameToFreePath_CreatesNewFileAndKeepsOriginal()
    {
        DemoFilePanelSource provider = Demo(
            ("source.txt", "new"), ("target.txt", "original"));
        var resolver = new SequenceResolver(new FileOperationConflictDecision
        {
            Mode = ConflictDecisionMode.Rename,
            NewDestinationPath = "/renamed.txt",
        });

        FileOperationResult result = await ExecuteProvider(
            provider, FileOperationKind.Copy, ["/source.txt"], "/target.txt", resolver);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("original", await ReadDemo(provider, "/target.txt"));
        Assert.Equal("new", await ReadDemo(provider, "/renamed.txt"));
    }

    [Fact]
    public async Task ProviderCopy_RenameToSource_IsRejectedWithoutChangingFiles()
    {
        DemoFilePanelSource provider = Demo(
            ("source.txt", "new"), ("target.txt", "original"));
        var resolver = new SequenceResolver(new FileOperationConflictDecision
        {
            Mode = ConflictDecisionMode.Rename,
            NewDestinationPath = "/source.txt",
        });

        await Assert.ThrowsAsync<IOException>(() => ExecuteProvider(
            provider, FileOperationKind.Copy, ["/source.txt"], "/target.txt", resolver));

        Assert.Equal("original", await ReadDemo(provider, "/target.txt"));
        Assert.Equal("new", await ReadDemo(provider, "/source.txt"));
    }

    [Fact]
    public async Task ProviderCopy_OverwriteDoesNotDeleteExistingDirectory()
    {
        DemoFilePanelSource provider = Demo(
            ("src/item.txt", "new"),
            ("dest/item.txt/nested.txt", "protected"));
        var resolver = new SequenceResolver(
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite));

        await Assert.ThrowsAsync<IOException>(() => ExecuteProvider(
            provider, FileOperationKind.Copy, ["/src/item.txt"], "/dest", resolver));

        Assert.True(provider.GetItem("/dest/item.txt")!.IsDirectory);
        Assert.Equal("protected", await ReadDemo(provider, "/dest/item.txt/nested.txt"));
    }

    [Theory]
    [InlineData(FileOperationKind.Copy, ConflictDecisionMode.SkipAll)]
    [InlineData(FileOperationKind.Copy, ConflictDecisionMode.OverwriteAll)]
    [InlineData(FileOperationKind.Copy, ConflictDecisionMode.RenameAll)]
    [InlineData(FileOperationKind.Move, ConflictDecisionMode.SkipAll)]
    [InlineData(FileOperationKind.Move, ConflictDecisionMode.OverwriteAll)]
    [InlineData(FileOperationKind.Move, ConflictDecisionMode.RenameAll)]
    public async Task ProviderCopyAndMove_StickyDecisionsAreReused(
        FileOperationKind kind, ConflictDecisionMode decision)
    {
        DemoFilePanelSource provider = Demo(
            ("src/a.txt", "new-a"),
            ("src/b.txt", "new-b"),
            ("dest/a.txt", "old-a"),
            ("dest/b.txt", "old-b"));
        var resolver = new SequenceResolver(FileOperationConflictDecision.FromMode(decision));

        if (kind == FileOperationKind.Copy && decision == ConflictDecisionMode.OverwriteAll)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                ExecuteProvider(provider, kind, ["/src/a.txt", "/src/b.txt"], "/dest", resolver));
            Assert.Equal(1, resolver.CallCount);
            Assert.Equal("old-a", await ReadDemo(provider, "/dest/a.txt"));
            Assert.Equal("old-b", await ReadDemo(provider, "/dest/b.txt"));
            Assert.Equal("new-a", await ReadDemo(provider, "/src/a.txt"));
            Assert.Equal("new-b", await ReadDemo(provider, "/src/b.txt"));
            return;
        }

        FileOperationResult result = await ExecuteProvider(
            provider, kind, ["/src/a.txt", "/src/b.txt"], "/dest", resolver);

        Assert.Equal(1, resolver.CallCount);
        if (decision == ConflictDecisionMode.SkipAll)
        {
            Assert.Equal(2, result.SkippedCount);
            Assert.Equal("old-a", await ReadDemo(provider, "/dest/a.txt"));
            Assert.Equal("old-b", await ReadDemo(provider, "/dest/b.txt"));
        }
        else if (decision == ConflictDecisionMode.OverwriteAll)
        {
            Assert.Equal("new-a", await ReadDemo(provider, "/dest/a.txt"));
            Assert.Equal("new-b", await ReadDemo(provider, "/dest/b.txt"));
        }
        else
        {
            Assert.Equal("old-a", await ReadDemo(provider, "/dest/a.txt"));
            Assert.Equal("old-b", await ReadDemo(provider, "/dest/b.txt"));
            Assert.Equal("new-a", await ReadDemo(provider, "/dest/a (2).txt"));
            Assert.Equal("new-b", await ReadDemo(provider, "/dest/b (2).txt"));
        }

        Assert.Equal(kind == FileOperationKind.Move && decision != ConflictDecisionMode.SkipAll,
            provider.GetItem("/src/a.txt") is null);
        Assert.Equal(kind == FileOperationKind.Move && decision != ConflictDecisionMode.SkipAll,
            provider.GetItem("/src/b.txt") is null);
    }

    [Theory]
    [InlineData(ConflictDecisionMode.SkipAll)]
    [InlineData(ConflictDecisionMode.OverwriteAll)]
    [InlineData(ConflictDecisionMode.RenameAll)]
    public async Task LocalMove_StickyDecisionsAreReused(ConflictDecisionMode decision)
    {
        string source = Path.Combine(_root, "source");
        string destination = Path.Combine(_root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        Write(source, "a.txt", "new-a");
        Write(source, "b.txt", "new-b");
        Write(destination, "a.txt", "old-a");
        Write(destination, "b.txt", "old-b");
        var resolver = new SequenceResolver(FileOperationConflictDecision.FromMode(decision));

        FileOperationResult result = await new FileOperationService().ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Move,
                Sources = [Path.Combine(source, "a.txt"), Path.Combine(source, "b.txt")],
                Destination = destination,
                Options = new FileOperationOptions(),
            }, null, resolver);

        Assert.Equal(1, resolver.CallCount);
        if (decision == ConflictDecisionMode.SkipAll)
        {
            Assert.Equal(2, result.SkippedCount);
            Assert.Equal("old-a", File.ReadAllText(Path.Combine(destination, "a.txt")));
            Assert.Equal("old-b", File.ReadAllText(Path.Combine(destination, "b.txt")));
        }
        else if (decision == ConflictDecisionMode.OverwriteAll)
        {
            Assert.Equal("new-a", File.ReadAllText(Path.Combine(destination, "a.txt")));
            Assert.Equal("new-b", File.ReadAllText(Path.Combine(destination, "b.txt")));
        }
        else
        {
            Assert.Equal("new-a", File.ReadAllText(Path.Combine(destination, "a (2).txt")));
            Assert.Equal("new-b", File.ReadAllText(Path.Combine(destination, "b (2).txt")));
        }
    }

    [Fact]
    public async Task LocalCopy_CaseOnlyDifferentTargetIsNotSamePath()
    {
        if (!CaseSensitiveFileSystem())
            return;

        string source = Write(_root, "item.txt", "data");
        string target = Path.Combine(_root, "ITEM.txt");
        FileOperationResult result = await ExecuteLocal(
            FileOperationKind.Copy, [source], target);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("data", File.ReadAllText(source));
        Assert.Equal("data", File.ReadAllText(target));
    }

    [Fact]
    public async Task LocalMove_CaseOnlyRenameIsNotSkipped()
    {
        if (!CaseSensitiveFileSystem())
            return;

        string source = Write(_root, "item.txt", "data");
        string destination = Path.Combine(_root, "ITEM.txt");
        await ExecuteLocal(FileOperationKind.Move, [source], "ITEM.txt");

        Assert.False(File.Exists(source));
        Assert.Equal("data", File.ReadAllText(destination));
    }

    [Fact]
    public async Task LocalCopy_WildcardTargetsDifferingOnlyByCaseDoNotCollide()
    {
        if (!CaseSensitiveFileSystem())
            return;

        string sourceFolder = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceFolder);
        string destinationFolder = Path.Combine(_root, "destination");
        Directory.CreateDirectory(destinationFolder);
        string lower = Write(sourceFolder, "item.txt", "lower");
        string upper = Write(sourceFolder, "ITEM.txt", "upper");

        FileOperationResult result = await ExecuteLocal(
            FileOperationKind.Copy, [lower, upper], Path.Combine(destinationFolder, "*.bak"));

        Assert.Equal(2, result.CopiedCount);
        Assert.Equal("lower", File.ReadAllText(Path.Combine(destinationFolder, "item.bak")));
        Assert.Equal("upper", File.ReadAllText(Path.Combine(destinationFolder, "ITEM.bak")));
    }

    [Fact]
    public async Task LocalMove_CaseDistinctSiblingIsNotTreatedAsDescendant()
    {
        if (!CaseSensitiveFileSystem())
            return;

        string source = Path.Combine(_root, "folder");
        string other = Path.Combine(_root, "FOLDER");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(other);
        Write(source, "child.txt", "data");

        await ExecuteLocal(FileOperationKind.Move, [source], other);

        Assert.False(Directory.Exists(source));
        Assert.Equal("data", File.ReadAllText(Path.Combine(other, "folder", "child.txt")));
    }

    private bool CaseSensitiveFileSystem()
    {
        string probe = Write(_root, "case-sensitivity-probe.txt", "probe");
        return !File.Exists(Path.Combine(_root, "CASE-SENSITIVITY-PROBE.txt"));
    }

    private static Task<FileOperationResult> ExecuteProvider(
        DemoFilePanelSource provider, FileOperationKind kind, string[] sources,
        string destination, IFileOperationConflictResolver resolver) =>
        new FileOperationService(new FilePanelSourceRegistry([provider])).ExecuteAsync(
            new FileOperationRequest
            {
                Kind = kind,
                Sources = [],
                SourceLocations = sources.Select(PanelLocation.Demo).ToArray(),
                DestinationLocation = PanelLocation.Demo(destination),
                Options = new FileOperationOptions(),
            }, null, resolver);

    private static Task<FileOperationResult> ExecuteLocal(
        FileOperationKind kind, string[] sources, string destination) =>
        new FileOperationService().ExecuteAsync(
            new FileOperationRequest
            {
                Kind = kind,
                Sources = sources,
                Destination = destination,
                Options = new FileOperationOptions(),
            }, null, new SequenceResolver());

    private DemoFilePanelSource Demo(params (string Path, string Content)[] entries)
    {
        string path = Path.Combine(_root, $"demo-{Guid.NewGuid():N}");
        foreach ((string relative, string value) in entries)
            Write(path, relative.Replace('/', Path.DirectorySeparatorChar), value);
        return DemoFilePanelSource.ImportFromDirectory(path);
    }

    private static string Write(string folder, string name, string content)
    {
        string path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task<string> ReadDemo(DemoFilePanelSource provider, string path)
    {
        await using Stream stream = await provider.OpenReadAsync(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed class SequenceResolver(params FileOperationConflictDecision[] decisions)
        : IFileOperationConflictResolver
    {
        private readonly List<FileOperationConflict> _conflicts = [];
        public int CallCount => _conflicts.Count;
        public IReadOnlyList<FileOperationConflict> Conflicts => _conflicts;

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            _conflicts.Add(conflict);
            if (CallCount > decisions.Length)
                throw new InvalidOperationException("Unexpected conflict.");
            return decisions[CallCount - 1];
        }
    }
}
