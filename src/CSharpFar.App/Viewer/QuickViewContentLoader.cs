using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Core.Text;

namespace CSharpFar.App.Viewer;

internal sealed record QuickViewContent(
    string? Error = null,
    DateTime? Created = null, DateTime? Modified = null, DateTime? Accessed = null,
    FileAttributes Attributes = default, int Files = 0, int Directories = 0,
    string[]? Lines = null, TextFileInfo? TextInfo = null)
{
    internal static QuickViewContent ReadLocal(FilePanelItem item)
    {
        try
        {
            if (item.IsDirectory)
            {
                var info = new DirectoryInfo(item.FullPath);
                if (!info.Exists) return new("Access denied");
                int files = 0, directories = 0;
                try
                {
                    foreach (var entry in info.EnumerateFileSystemInfos())
                        if ((entry.Attributes & FileAttributes.Directory) != 0) directories++;
                        else files++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return new(Created: info.CreationTime, Modified: info.LastWriteTime,
                    Accessed: info.LastAccessTime, Attributes: info.Attributes, Files: files, Directories: directories);
            }
            if (new FileInfo(item.FullPath).Length > TextFileReader.MaxFileSizeBytes)
                return new("File is too large to preview");
            return new(Lines: TextFileReader.ReadLines(item.FullPath),
                TextInfo: TextFileInfo.IsTextFile(item.FullPath) ? TextFileInfo.Read(item.FullPath) : null);
        }
        catch (Exception ex) { return new(ex.Message); }
    }

    internal static async Task<QuickViewContent> ReadAsync(IFilePanelSource source, FilePanelItem item, CancellationToken token)
    {
        if (item.SourceId == PanelSourceId.Local)
            return ReadLocal(item);
        if (item.IsDirectory)
        {
            var entries = await source.AsAsync().EnumerateDirectoryAsync(item.SourcePath, token).ConfigureAwait(false);
            return new(Modified: item.LastWriteTime, Attributes: item.Attributes,
                Files: entries.Count(i => !i.IsDirectory),
                Directories: entries.Count(i => i.IsDirectory && !i.IsParentDirectory));
        }
        if (item.Size > TextFileReader.MaxFileSizeBytes)
            return new("File is too large to preview");
        await using var stream = await source.AsAsync().OpenReadAsync(item.SourcePath, token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            int count = await stream.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > TextFileReader.MaxFileSizeBytes)
                return new("File is too large to preview");
            buffer.Write(chunk, 0, count);
        }
        byte[] bytes = buffer.ToArray();
        var detection = TextEncodingDetector.Detect(bytes[..Math.Min(bytes.Length, 65536)]);
        using var reader = new StreamReader(new MemoryStream(bytes, detection.ContentStartLength,
            bytes.Length - detection.ContentStartLength), detection.Encoding, detectEncodingFromByteOrderMarks: false);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return new(Lines: lines.ToArray());
    }
}

/// <summary>One active worker and one latest pending preview; UI owns state.</summary>
internal sealed class QuickViewContentLoader(FilePanelSourceRegistry sources, Action<Action> postToUi) : IDisposable
{
    private (PanelLocation Location, DateTime Modified, long? Size, long Revision)? _key;
    private FilePanelItem? _pending;
    private CancellationTokenSource? _active;
    private long _generation;
    private bool _disposed;
    public QuickViewContent? Current { get; private set; }

    public QuickViewContent? Update(FilePanelItem? item, long revision = 0)
    {
        if (_disposed) return null;
        if (item?.IsParentDirectory == true) item = null;
        var key = item is null ? ((PanelLocation, DateTime, long?, long)?)null
            : (item.Location, item.LastWriteTime, item.Size, revision);
        if (key == _key) return Current;
        _key = key;
        _generation++;
        Current = null;
        _pending = item;
        if (_active is not null) _ = CancelAsync(_active);
        else StartNext();
        return Current;
    }

    private static async Task CancelAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
    }

    private void StartNext()
    {
        if (_disposed || _pending is not { } item) return;
        _pending = null;
        if (!sources.TryAcquireUsage(item.SourceId, out var source, out var lease))
        {
            Current = new("Source unavailable");
            return;
        }
        var cancellation = _active = new CancellationTokenSource();
        var token = cancellation.Token;
        long generation = _generation;
        _ = Task.Run(async () =>
        {
            QuickViewContent result;
            try { result = await QuickViewContent.ReadAsync(source, item, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { result = new("Cancelled"); }
            catch (Exception ex) { result = new(ex.Message); }
            finally
            {
                try { lease.Dispose(); }
                catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
            }

            postToUi(() =>
            {
                _active = null;
                cancellation.Dispose();
                if (_disposed) return;
                if (generation == _generation && sources.TryGetSource(item.SourceId, out var current) &&
                    ReferenceEquals(source, current))
                    Current = result;
                StartNext();
            });
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _pending = null;
        if (_active is not null) _ = CancelAsync(_active);
    }
}
