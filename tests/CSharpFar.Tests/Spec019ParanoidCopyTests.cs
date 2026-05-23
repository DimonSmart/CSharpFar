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

    [Fact]
    public async Task ParanoidCopy_AutomaticallyRetriesAnalyzerSourceReadFailure()
    {
        string source = CreateDeterministicFile(_source, "analyzer-retry.bin", 3 * 1024 * 1024);
        string destination = Path.Combine(_destination, "analyzer-retry.bin");
        CopyPrefix(source, destination, 1024 * 1024);
        var resolver = new RecordingConflictResolver(ConflictDecisionMode.Skip);
        var retryDelays = new List<TimeSpan>();
        int analyzeCalls = 0;
        var dependencies = FileOperationServiceDependencies.Default with
        {
            DelayAsync = (delay, _) =>
            {
                retryDelays.Add(delay);
                return Task.CompletedTask;
            },
            AnalyzeResume = (sourcePath, destinationPath, sourceSnapshot, cancellationToken) =>
            {
                analyzeCalls++;
                if (analyzeCalls == 1)
                {
                    return CopyResumePlan.CannotResume(
                        sourceSnapshot?.Length ?? 0,
                        new FileInfo(destinationPath).Length,
                        "Source is temporarily unavailable.",
                        CopyResumeReadFailureSide.Source);
                }

                return new CopyResumeAnalyzer().Analyze(sourcePath, destinationPath, sourceSnapshot, cancellationToken);
            },
        };

        FileOperationResult result = await CopyWithParanoidAsync(
            [source],
            _destination,
            resolver,
            dependencies: dependencies);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(0, resolver.CallCount);
        Assert.Equal(new[] { TimeSpan.FromMinutes(1) }, retryDelays);
        Assert.True(analyzeCalls >= 2);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task ParanoidCopy_AutomaticallyRetriesTransientSourceReadFailure()
    {
        string source = CreateDeterministicFile(_source, "read-retry.bin", 3 * 1024 * 1024);
        string destination = Path.Combine(_destination, "read-retry.bin");
        var resolver = new RecordingConflictResolver(ConflictDecisionMode.Skip);
        var retryDelays = new List<TimeSpan>();
        int readFailuresRemaining = 1;
        var dependencies = FileOperationServiceDependencies.Default with
        {
            DelayAsync = (delay, _) =>
            {
                retryDelays.Add(delay);
                return Task.CompletedTask;
            },
            OpenFileStream = (path, mode, access, share, bufferSize, options) =>
            {
                Stream stream = FileOperationServiceDependencies.Default.OpenFileStream(path, mode, access, share, bufferSize, options);
                if (path == source && access == FileAccess.Read && readFailuresRemaining > 0)
                {
                    return new ThrowingReadStream(
                        stream,
                        throwAtOrAfterPosition: 1024 * 1024,
                        onThrow: () => readFailuresRemaining--);
                }

                return stream;
            },
        };

        FileOperationResult result = await CopyWithParanoidAsync(
            [source],
            _destination,
            resolver,
            dependencies: dependencies);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(0, resolver.CallCount);
        Assert.Equal(new[] { TimeSpan.FromMinutes(1) }, retryDelays);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task ParanoidCopy_DoesNotRollbackAcrossRepeatedSourceReadFailures()
    {
        string source = CreateDeterministicFile(_source, "repeated-read-retry.bin", 3 * 1024 * 1024);
        string destination = Path.Combine(_destination, "repeated-read-retry.bin");
        var retryDelays = new List<TimeSpan>();
        var progress = new List<FileOperationProgress>();
        int readFailuresRemaining = 2;
        var dependencies = FileOperationServiceDependencies.Default with
        {
            DelayAsync = (delay, _) =>
            {
                retryDelays.Add(delay);
                return Task.CompletedTask;
            },
            OpenFileStream = (path, mode, access, share, bufferSize, options) =>
            {
                Stream stream = FileOperationServiceDependencies.Default.OpenFileStream(path, mode, access, share, bufferSize, options);
                if (path == source && access == FileAccess.Read && readFailuresRemaining > 0)
                {
                    return new ThrowingReadStream(
                        stream,
                        throwAtOrAfterPosition: 1024 * 1024,
                        onThrow: () => readFailuresRemaining--);
                }

                return stream;
            },
        };

        FileOperationResult result = await CopyWithParanoidAsync(
            [source],
            _destination,
            progress: progress,
            dependencies: dependencies);

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal(new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1) }, retryDelays);
        Assert.DoesNotContain(progress, p => p.ResumeRollbackBytes > 0);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
    }

    [Fact]
    public async Task ParanoidCopy_WriteFailureIsNotAutomaticallyRetried()
    {
        string source = CreateDeterministicFile(_source, "write-failure.bin", 3 * 1024 * 1024);
        string destination = Path.Combine(_destination, "write-failure.bin");
        var retryDelays = new List<TimeSpan>();
        var dependencies = FileOperationServiceDependencies.Default with
        {
            DelayAsync = (delay, _) =>
            {
                retryDelays.Add(delay);
                return Task.CompletedTask;
            },
            OpenFileStream = (path, mode, access, share, bufferSize, options) =>
            {
                Stream stream = FileOperationServiceDependencies.Default.OpenFileStream(path, mode, access, share, bufferSize, options);
                return path == destination && access != FileAccess.Read
                    ? new ThrowingWriteStream(stream)
                    : stream;
            },
        };

        await Assert.ThrowsAsync<IOException>(() => CopyWithParanoidAsync(
            [source],
            _destination,
            dependencies: dependencies));

        Assert.Empty(retryDelays);
    }

    private static async Task<FileOperationResult> CopyWithParanoidAsync(
        IReadOnlyList<string> sources,
        string destination,
        IFileOperationConflictResolver? resolver = null,
        List<FileOperationProgress>? progress = null,
        FileOperationServiceDependencies? dependencies = null)
    {
        var service = dependencies is null
            ? new FileOperationService()
            : new FileOperationService(dependencies);

        return await service.ExecuteAsync(
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

    private sealed class ThrowingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _throwAtOrAfterPosition;
        private readonly Action _onThrow;
        private bool _thrown;

        public ThrowingReadStream(Stream inner, long throwAtOrAfterPosition, Action onThrow)
        {
            _inner = inner;
            _throwAtOrAfterPosition = throwAtOrAfterPosition;
            _onThrow = onThrow;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (ShouldThrowBeforeRead())
                ThrowReadFailure();

            count = LimitCountBeforeFailure(count);
            return _inner.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (ShouldThrowBeforeRead())
                ThrowReadFailure();

            int count = LimitCountBeforeFailure(buffer.Length);
            return await _inner.ReadAsync(buffer[..count], cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        private bool ShouldThrowBeforeRead() =>
            !_thrown && _inner.Position >= _throwAtOrAfterPosition;

        private int LimitCountBeforeFailure(int count)
        {
            if (_thrown || _inner.Position >= _throwAtOrAfterPosition)
                return count;

            long bytesBeforeFailure = _throwAtOrAfterPosition - _inner.Position;
            return (int)Math.Min(count, bytesBeforeFailure);
        }

        private void ThrowReadFailure()
        {
            _thrown = true;
            _onThrow();
            throw new IOException("Transient source read failure.");
        }
    }

    private sealed class ThrowingWriteStream : Stream
    {
        private readonly Stream _inner;

        public ThrowingWriteStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Destination write failed.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("Destination write failed.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
