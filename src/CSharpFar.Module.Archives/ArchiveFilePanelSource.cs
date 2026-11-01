using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Module.Archives;

/// <summary>
/// A read-only, disposable archive session. Only metadata is indexed; file
/// contents are decompressed on demand into caller-owned bounded streams.
/// </summary>
public sealed class ArchiveFilePanelSource : IFilePanelSource, ITemporaryFilePanelSource, IAsyncDisposable
{
    public const long MaxTemporaryBytes = 1024L * 1024 * 1024;
    public const int MaxNestingDepth = 3;
    private static readonly Guid ModuleId = new("e772e934-2f54-49e5-b9c2-31ab4188a307");
    private static readonly char[] Separators = ['/'];
    private readonly string _archiveFile;
    private readonly bool _ownsFile;
    private readonly long _fileLength;
    private readonly DateTime _fileModifiedUtc;
    private readonly ArchiveEntryIndex _index;
    private bool _disposed;

    private ArchiveFilePanelSource(
        string archiveFile, bool ownsFile, long fileLength, DateTime modifiedUtc,
        string format, ArchiveEntryIndex index, int depth)
    {
        _archiveFile = archiveFile;
        _ownsFile = ownsFile;
        _fileLength = fileLength;
        _fileModifiedUtc = modifiedUtc;
        _index = index;
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
        PanelProviderCapabilities.Refresh;
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
                file, !local, snapshot.Length, snapshot.LastWriteTimeUtc, format, index, depth);
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

    public Task<Stream> OpenWriteAsync(
        string sourcePath, bool overwrite, CancellationToken cancellationToken = default) =>
        throw ReadOnly();
    public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        throw ReadOnly();
    public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) =>
        throw ReadOnly();
    public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new("Archive provider is read-only.");

    private void EnsureFresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
