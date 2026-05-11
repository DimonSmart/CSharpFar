using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class Spec019ParanoidCopyTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _destination;

    public Spec019ParanoidCopyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec019_{Guid.NewGuid():N}");
        _source = Path.Combine(_root, "source");
        _destination = Path.Combine(_root, "destination");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_destination);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ParanoidCopy_ProducesExactFile_WhenPartialFileIsValid()
    {
        string source = CreateDeterministicFile(_source, "large.bin", 3 * 1024 * 1024);
        string destination = Path.Combine(_destination, "large.bin");
        CopyPrefix(source, destination, 1536 * 1024);
        var progress = new List<FileOperationProgress>();

        FileOperationResult result = await CopyWithParanoidAsync(
            [source],
            _destination,
            progress: progress);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
        Assert.Contains(progress, p =>
            p.Phase == FileOperationPhase.Validating &&
            p.StatusMessage == "Tail validation passed" &&
            p.ResumeOffset == 1536 * 1024 &&
            p.ResumeRollbackBytes == 0);
        Assert.Contains(progress, p =>
            p.Phase == FileOperationPhase.Copying &&
            p.CurrentBytesDone >= 1536 * 1024 &&
            p.TotalBytesDone >= 1536 * 1024);
    }

    [Fact]
    public async Task ParanoidCopy_ProducesExactFile_WhenPartialTailIsCorrupted()
    {
        string source = CreateDeterministicFile(_source, "large.bin", 8 * 1024 * 1024);
        string destination = Path.Combine(_destination, "large.bin");
        CopyPrefix(source, destination, 6 * 1024 * 1024);
        CorruptRange(destination, startOffset: (6 * 1024 * 1024) - (32 * 1024), length: 32 * 1024);
        var progress = new List<FileOperationProgress>();

        FileOperationResult result = await CopyWithParanoidAsync(
            [source],
            _destination,
            progress: progress);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
        Assert.Contains(progress, p =>
            p.Phase == FileOperationPhase.Validating &&
            p.StatusMessage == "Tail mismatch detected" &&
            p.ResumeOffset.HasValue &&
            p.ResumeRollbackBytes > 0);
    }

    [Fact]
    public async Task ParanoidCopy_AppliesToFilesInsideCopiedDirectory()
    {
        string sourceFile = CreateDeterministicFile(_source, "nested.bin", 2 * 1024 * 1024);
        string copiedDirectory = Path.Combine(_destination, "source");
        Directory.CreateDirectory(copiedDirectory);
        string destinationFile = Path.Combine(copiedDirectory, "nested.bin");
        CopyPrefix(sourceFile, destinationFile, 1024 * 1024);

        await CopyWithParanoidAsync([_source], _destination);

        Assert.Equal(File.ReadAllBytes(sourceFile), File.ReadAllBytes(destinationFile));
    }

    [Fact]
    public async Task ParanoidCopy_FallsBackToConflictDecision_WhenResumeIsUnsafe()
    {
        string source = CreateDeterministicFile(_source, "unsafe.bin", 4 * 1024 * 1024);
        string destination = Path.Combine(_destination, "unsafe.bin");
        byte[] destinationBytes = Enumerable.Repeat((byte)255, 2 * 1024 * 1024).ToArray();
        File.WriteAllBytes(destination, destinationBytes);
        var resolver = new RecordingConflictResolver(ConflictDecisionMode.Skip);

        FileOperationResult result = await CopyWithParanoidAsync(
            [source],
            _destination,
            resolver);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(destinationBytes, File.ReadAllBytes(destination));
    }

    private static async Task<FileOperationResult> CopyWithParanoidAsync(
        IReadOnlyList<string> sources,
        string destination,
        IFileOperationConflictResolver? resolver = null,
        List<FileOperationProgress>? progress = null)
    {
        return await new FileOperationService().ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = sources,
                Destination = destination,
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.ResumeWithTailValidation,
                },
            },
            progress is null ? null : new Progress<FileOperationProgress>(progress.Add),
            resolver ?? new RecordingConflictResolver(ConflictDecisionMode.Overwrite));
    }

    private static string CreateDeterministicFile(string directory, string name, int length)
    {
        string path = Path.Combine(directory, name);
        byte[] bytes = new byte[length];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(i % 251);

        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void CopyPrefix(string source, string destination, int length)
    {
        byte[] bytes = File.ReadAllBytes(source);
        File.WriteAllBytes(destination, bytes[..length]);
    }

    private static void CorruptRange(string path, int startOffset, int length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = startOffset;
        for (int i = 0; i < length; i++)
            stream.WriteByte(255);
    }

    private sealed class RecordingConflictResolver : IFileOperationConflictResolver
    {
        private readonly ConflictDecisionMode _mode;

        public RecordingConflictResolver(ConflictDecisionMode mode)
        {
            _mode = mode;
        }

        public int CallCount { get; private set; }

        public FileOperationConflictDecision Resolve(FileOperationConflict conflict)
        {
            CallCount++;
            return FileOperationConflictDecision.FromMode(_mode);
        }
    }
}
