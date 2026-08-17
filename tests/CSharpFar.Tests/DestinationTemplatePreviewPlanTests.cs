using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class DestinationTemplatePreviewPlanTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"CSharpFarPreview_{Guid.NewGuid():N}");
    private readonly string _source;
    private readonly string _destination;

    public DestinationTemplatePreviewPlanTests()
    {
        _source = Path.Combine(_root, "source");
        _destination = Path.Combine(_root, "destination");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_destination);
    }

    [Fact]
    public async Task BuildPlan_TemplatePreviewIsReadOnlyAndPredictsCopyExecution()
    {
        string first = Write("foo.txt", "first", new DateTime(2026, 8, 12));
        string second = Write("bar.csv", "second", new DateTime(2026, 8, 13));
        var request = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [first, second],
            Destination = Path.Combine(_destination, "{modified:yyyy-MM-dd}", "{name}_OLD{ext}"),
            UseDestinationTemplate = true,
            Options = new FileOperationOptions(),
        };
        var service = new FileOperationService();

        FileOperationPlan preview = service.BuildPlan(request);

        Assert.Equal(2, preview.Items.Count);
        Assert.All(preview.Items, item => Assert.False(File.Exists(item.Destination.SourcePath)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_destination));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));

        await service.ExecuteAsync(request, null, new TestConflictResolver());

        Assert.All(preview.Items, item => Assert.True(File.Exists(item.Destination.SourcePath)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Write(string name, string content, DateTime modified)
    {
        string path = Path.Combine(_source, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, modified);
        return path;
    }

    private sealed class TestConflictResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }
}
