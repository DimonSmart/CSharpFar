using System.Collections.Concurrent;
using System.Security.Cryptography;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Module.Archives;

/// <summary>
/// An indexed archive session. Writable local ZIP archives use copy-on-write batches.
/// Other formats and nested/remote archives retain the read-only v1 behavior.
/// </summary>
public sealed class ArchiveFilePanelSource : IFilePanelSource, IFileOperationBatchProvider, ITemporaryFilePanelSource, IAsyncDisposable
{
    public const long MaxTemporaryBytes = 1024L * 1024 * 1024;
    public const int MaxNestingDepth = 3;
    private static readonly Guid ModuleId = new("e772e934-2f54-49e5-b9c2-31ab4188a307");
    private static readonly char[] Separators = ['/'];
    private static readonly ConcurrentDictionary<string, long> Generations =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _archiveFile;
    private readonly bool _ownsFile;
    private long _fileLength;
    private DateTime _fileModifiedUtc;
    private ArchiveEntryIndex _index;
    private long _generation;
    private readonly bool _writable;
    private bool _disposed;

    private ArchiveFilePanelSource(
        string archiveFile, bool ownsFile, long fileLength, DateTime modifiedUtc,
        string format, ArchiveEntryIndex index, int depth, bool writable)
    {
        _archiveFile = archiveFile;
        _ownsFile = ownsFile;
        _fileLength = fileLength;
        _fileModifiedUtc = modifiedUtc;
        _index = index;
        _writable = writable;
        _generation = Generations.GetOrAdd(archiveFile, 0);
        Format = format;
        Depth = depth;
        SourceId = PanelSourceId.Module(ModuleId, Guid.NewGuid().ToString("N"));
    }

    public int Depth { get; }
    public string Format { get; }
    public PanelSourceId SourceId { get; }
    public string DisplayName => Format;
    public bool IsTemporary => true;
    public PanelProviderCapabilities Capabilities =>
        PanelProviderCapabilities.Enumerate |
        PanelProviderCapabilities.OpenRead |
        PanelProviderCapabilities.CopyFrom |
        PanelProviderCapabilities.Refresh |
        (_writable ?
            PanelProviderCapabilities.OpenWrite | PanelProviderCapabilities.CreateFile |
            PanelProviderCapabilities.CreateDirectory | PanelProviderCapabilities.Delete |
            PanelProviderCapabilities.Rename | PanelProviderCapabilities.CopyTo |
            PanelProviderCapabilities.MoveFrom | PanelProviderCapabilities.MoveTo |
            PanelProviderCapabilities.Edit : PanelProviderCapabilities.None);

    internal string ArchiveFile => _archiveFile;
    internal ArchiveEntryIndex Index => _index;
    internal bool IsWritable => _writable;
    internal void CheckFresh() => EnsureFresh();

    internal void Published(ArchiveEntryIndex index)
    {
        // The temporary result has already passed format validation. Publication
        // is irreversible at this point; don't discard that success on refresh.
        var info = new FileInfo(_archiveFile);
        _index = index;
        _fileLength = info.Length;
        _fileModifiedUtc = info.LastWriteTimeUtc;
        _generation = Generations.AddOrUpdate(_archiveFile, 1, (_, previous) => previous + 1);
    }

    internal Task<Stream> OpenOriginalEntryAsync(
        ArchiveNode entry, CancellationToken cancellationToken) =>
        Task.Run<Stream>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFresh();
            return SharpCompressArchiveReader.OpenEntry(
                _archiveFile, Format, entry, cancellationToken);
        }, cancellationToken);
    public IReadOnlyCollection<char> PathSeparators => Separators;

    public static bool IsSupportedName(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    public static async Task<ArchiveFilePanelSource> CreateAsync(
        IFilePanelSource original, PanelLocation location, string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (!IsSupportedName(name))
            throw new NotSupportedException("Archive format is not supported.");

        int depth = original is ArchiveFilePanelSource archive ? archive.Depth + 1 : 1;
        if (depth > MaxNestingDepth)
            throw new NotSupportedException($"Nested archives are limited to {MaxNestingDepth} levels.");

        bool local = original.SourceId == PanelSourceId.Local;
        string file = local ? Path.GetFullPath(location.SourcePath)
            : Path.Combine(Path.GetTempPath(), "csharpfar-archive-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (!local)
            {
                await using var input = await original.OpenReadAsync(location.SourcePath, cancellationToken)
                    .ConfigureAwait(false);
                await using var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                byte[] buffer = new byte[65536];
                long stored = 0;
                while (true)
                {
                    int n = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (n == 0)
                        break;
                    stored = checked(stored + n);
                    if (stored > MaxTemporaryBytes)
                        throw new InvalidDataException(
                            $"Archive needs more than {MaxTemporaryBytes / 1024 / 1024} MiB temporary space.");
                    await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            FileInfo snapshot = new(file);
            if (!snapshot.Exists)
                throw new FileNotFoundException("The archive file is missing.", file);
            var (format, index) = await Task.Run(
                () => SharpCompressArchiveReader.BuildIndex(file, name, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            FileInfo after = new(file);
            if (snapshot.Length != after.Length || snapshot.LastWriteTimeUtc != after.LastWriteTimeUtc)
                throw new IOException("Archive changed while its index was being built.");

            return new ArchiveFilePanelSource(
                file, !local, snapshot.Length, snapshot.LastWriteTimeUtc, format, index, depth,
                local && !snapshot.IsReadOnly && snapshot.LinkTarget is null &&
                (format == "ZIP" ? ArchiveZipSafety.IsRebuildSafe(file) :
                 format is "TAR" or "TAR.GZ" && ArchiveTarSafety.IsRebuildSafe(file, format)));
        }
        catch
        {
            if (!local)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    public string NormalizePath(string sourcePath) => ArchivePath.NormalizePanel(sourcePath);
    public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "/";
    public string? GetParentPath(string sourcePath) => ArchivePath.Parent(sourcePath);

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFresh();
        return _index.Children(sourcePath, SourceId);
    }

    public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFresh();
        var entry = _index.Find(sourcePath);
        return entry is null || entry.Path == "/" ? null : ArchiveEntryIndex.ToItem(entry, SourceId);
    }

    public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFresh();
        var entry = _index.Find(sourcePath);
        if (entry is null || entry.IsDirectory)
            throw new FileNotFoundException("Archive file not found.", sourcePath);

        return Task.Run<Stream>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureFresh();
                return SharpCompressArchiveReader.OpenEntry(
                    _archiveFile, Format, entry, cancellationToken);
            }, cancellationToken);
    }

    public async ValueTask<IFileOperationBatch> BeginBatchAsync(
        FileOperationPlan plan, CancellationToken cancellationToken = default)
    {
        if (!_writable)
            throw ReadOnly();
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFresh();
        if (Format == "ZIP")
            ArchiveZipSafety.Validate(_archiveFile);
        else
            ArchiveTarSafety.Validate(_archiveFile, Format);
        byte[] originalDigest = await Task.Run(() =>
        {
            using var input = File.OpenRead(_archiveFile);
            return SHA256.HashData(input);
        }, cancellationToken).ConfigureAwait(false);
        EnsureFresh();
        return new ArchiveMutationBatch(this, originalDigest);
    }

    // All archive writes require an explicit batch. A bare stream cannot express
    // rollback after a caller exception, so don't advertise implicit auto-commit.
    public Task<Stream> OpenWriteAsync(
        string sourcePath, bool overwrite, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Archive writes require an explicit mutation batch.");

    public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        SingleMutationAsync(sourcePath, (batch, ct) => batch.CreateDirectoryAsync(sourcePath, ct), cancellationToken);

    public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) =>
        SingleMutationAsync(sourcePath, (batch, ct) => batch.DeleteAsync(sourcePath, recursive, ct), cancellationToken);

    public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) =>
        SingleMutationAsync(sourcePath, (batch, ct) => batch.RenameAsync(sourcePath, newSourcePath, ct), cancellationToken);

    private async Task SingleMutationAsync(
        string path, Func<IFileOperationBatch, CancellationToken, Task> mutate, CancellationToken cancellationToken)
    {
        var location = new PanelLocation(SourceId, path);
        await using var batch = await BeginBatchAsync(
            new FileOperationPlan([new FileOperationPlanItem(location, location, false)]),
            cancellationToken).ConfigureAwait(false);
        try
        {
            await mutate(batch, cancellationToken).ConfigureAwait(false);
            await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await batch.AbortAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static NotSupportedException ReadOnly() =>
        new("This archive is read-only or contains metadata that cannot be safely rewritten.");

    private void EnsureFresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Generations.GetOrAdd(_archiveFile, 0) != _generation)
            throw new IOException("Archive changed in another panel. Reopen it.");
        FileInfo info = new(_archiveFile);
        if (!info.Exists || info.Length != _fileLength || info.LastWriteTimeUtc != _fileModifiedUtc)
            throw new IOException("Archive was modified or removed since it was opened. Reopen it.");
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_ownsFile)
                File.Delete(_archiveFile);
        }
        return ValueTask.CompletedTask;
    }
}
