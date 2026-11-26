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
                Sources = [PanelLocation.Local(source)],
                Destination = PanelLocation.Local(target),
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
                Sources = [PanelLocation.Local(source)],
                Destination = PanelLocation.Local(target),
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
                Sources = [PanelLocation.Demo("/source.txt")],
                Destination = PanelLocation.Demo("/renamed.txt"),
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
                Sources = [PanelLocation.Demo("/source.txt")],
                Destination = PanelLocation.Demo("/renamed.txt"),
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

    [Fact]
    public async Task LocalCopy_SingleFileBesideOriginal_KeepsBothFiles()
    {
        string source = Path.Combine(_root, "report.txt");
        string target = Path.Combine(_root, "report (copy).txt");
        await File.WriteAllTextAsync(source, "original");

        FileOperationResult result = await new FileOperationService().ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [PanelLocation.Local(source)],
                Destination = PanelLocation.Local(target),
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new RecordingConflictResolver(ConflictDecisionMode.Rename));

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("original", await File.ReadAllTextAsync(source));
        Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task LocalCopy_SeveralFilesBesideOriginal_UsesDestinationTemplate()
    {
        string first = Path.Combine(_root, "first.txt");
        string second = Path.Combine(_root, "second.log");
        await File.WriteAllTextAsync(first, "one");
        await File.WriteAllTextAsync(second, "two");

        FileOperationResult result = await new FileOperationService().ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [PanelLocation.Local(first), PanelLocation.Local(second)],
                Destination = PanelLocation.Local(Path.Combine(_root, "{name} (copy){ext}")),
                UseDestinationTemplate = true,
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new RecordingConflictResolver(ConflictDecisionMode.Rename));

        Assert.Equal(2, result.CopiedCount);
        Assert.Equal("one", await File.ReadAllTextAsync(Path.Combine(_root, "first (copy).txt")));
        Assert.Equal("two", await File.ReadAllTextAsync(Path.Combine(_root, "second (copy).log")));
        Assert.Equal("one", await File.ReadAllTextAsync(first));
        Assert.Equal("two", await File.ReadAllTextAsync(second));
    }

    [Fact]
    public async Task LocalCopy_DirectoryBesideOriginal_UsesDestinationTemplate()
    {
        string source = Path.Combine(_root, "folder");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "child.txt"), "child");

        await new FileOperationService().ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [PanelLocation.Local(source)],
                Destination = PanelLocation.Local(Path.Combine(_root, "{name} (copy){ext}")),
                UseDestinationTemplate = true,
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new RecordingConflictResolver(ConflictDecisionMode.Rename));

        Assert.Equal("child", await File.ReadAllTextAsync(Path.Combine(_root, "folder (copy)", "child.txt")));
        Assert.Equal("child", await File.ReadAllTextAsync(Path.Combine(source, "child.txt")));
    }

    [Fact]
    public async Task ProviderCopy_FileBesideOriginal_UsesExactTargetName()
    {
        string fixture = CreateFixture(("report.txt", "original"));
        var provider = DemoFilePanelSource.ImportFromDirectory(fixture);
        var service = new FileOperationService(new FilePanelSourceRegistry([provider]));

        FileOperationResult result = await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [PanelLocation.Demo("/report.txt")],
                Destination = PanelLocation.Demo("/report (copy).txt"),
                Options = new FileOperationOptions(),
            },
            progress: null,
            conflictResolver: new RecordingConflictResolver(ConflictDecisionMode.Rename));

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("original", await ReadAllTextAsync(provider, "/report.txt"));
        Assert.Equal("original", await ReadAllTextAsync(provider, "/report (copy).txt"));
    }

    [Theory]
    [InlineData("renamed.txt", "renamed.txt", false)]
    [InlineData("./renamed.txt", "renamed.txt", false)]
    [InlineData("../renamed.txt", "../renamed.txt", false)]
    [InlineData("backup/renamed.txt", "backup/renamed.txt", false)]
    [InlineData("backup/", "backup/source.txt", false)]
    [InlineData("*.bak", "source.bak", false)]
    [InlineData("{name}_copy{ext}", "source_copy.txt", true)]
    public async Task LocalCopy_RelativeDestination_PreviewAndExecutionUseSourceParent(
        string destination, string expected, bool useTemplate)
    {
        string parent = Path.Combine(_root, "source{literal}");
        Directory.CreateDirectory(parent);
        string source = Path.Combine(parent, "source.txt");
        string target = Path.GetFullPath(expected, parent);
        await File.WriteAllTextAsync(source, "original");
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [PanelLocation.Local(source)],
            Destination = PanelLocation.Local(destination),
            UseDestinationTemplate = useTemplate,
            Options = new FileOperationOptions(),
        };
        var service = new FileOperationService();

        Assert.Equal(target, Assert.Single(service.BuildPlan(request).Items).Destination.SourcePath);
        Assert.False(File.Exists(target));
        var result = await service.ExecuteAsync(request, null,
            new RecordingConflictResolver(ConflictDecisionMode.Overwrite));

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
        Assert.Equal("original", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task LocalCopy_RelativeExistingDirectory_KeepsOriginalName()
    {
        string source = Path.Combine(_root, "source.txt");
        string folder = Path.Combine(_root, "backup");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(source, "original");
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [PanelLocation.Local(source)],
            Destination = PanelLocation.Local("backup"),
            Options = new FileOperationOptions(),
        };
        var service = new FileOperationService();
        string target = Path.Combine(folder, "source.txt");
        Assert.Equal(target, Assert.Single(service.BuildPlan(request).Items).Destination.SourcePath);
        var result = await service.ExecuteAsync(request, null,
            new RecordingConflictResolver(ConflictDecisionMode.Overwrite));
        Assert.Empty(result.Errors);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    [Theory]
    [InlineData("renamed.txt", "renamed.txt", false)]
    [InlineData("./renamed.txt", "renamed.txt", false)]
    [InlineData("backup/renamed.txt", "backup/renamed.txt", false)]
    [InlineData("*.bak", "source.bak", false)]
    [InlineData("{name}_new{ext}", "source_new.txt", true)]
    public async Task LocalMove_RelativeDestination_PreviewAndExecutionUseSourceParent(
        string destination, string expected, bool useTemplate)
    {
        string parent = Path.Combine(_root, "source{literal}");
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(Path.Combine(parent, "backup"));
        string source = Path.Combine(parent, "source.txt");
        string target = Path.GetFullPath(expected, parent);
        await File.WriteAllTextAsync(source, "original");
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Move,
            Sources = [PanelLocation.Local(source)],
            Destination = PanelLocation.Local(destination),
            UseDestinationTemplate = useTemplate,
            Options = new FileOperationOptions(),
        };
        var service = new FileOperationService();

        Assert.Equal(target, Assert.Single(service.BuildPlan(request).Items).Destination.SourcePath);
        Assert.False(File.Exists(target));
        var result = await service.ExecuteAsync(request, null,
            new RecordingConflictResolver(ConflictDecisionMode.Overwrite));

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.MovedCount);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(source));
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
