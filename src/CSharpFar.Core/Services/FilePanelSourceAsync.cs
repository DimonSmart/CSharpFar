using System.Runtime.CompilerServices;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Core.Services;

/// <summary>
/// Adapts legacy SDK sources without changing their identity or registration.
/// Use the original source for optional capability interfaces and lifetime leases.
/// </summary>
public static class FilePanelSourceAsync
{
    private static readonly ConditionalWeakTable<IFilePanelSource, SemaphoreSlim> Slots = new();
    private static readonly ConditionalWeakTable<IFilePanelSource, LegacyAdapter> Adapters = new();

    public static IAsyncFilePanelSource AsAsync(this IFilePanelSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source as IAsyncFilePanelSource ?? Adapters.GetValue(source, static value => new LegacyAdapter(value));
    }

    /// <summary>
    /// Runs synchronous I/O on a worker, with at most two active fallback
    /// operations per source. Cancellation while waiting for a slot does not
    /// start the operation. Once started, the actual outcome is observed: in
    /// particular, a successful write or returned stream must not be discarded.
    /// </summary>
    public static Task<T> RunBlockingAsync<T>(
        IFilePanelSource source, Func<T> operation, CancellationToken cancellationToken = default) =>
        RunLegacyAsync(source, () => Task.FromResult(operation()), cancellationToken);

    public static Task RunBlockingAsync(
        IFilePanelSource source, Action operation, CancellationToken cancellationToken = default) =>
        RunBlockingAsync(source, () => { operation(); return true; }, cancellationToken);

    private static async Task<T> RunLegacyAsync<T>(
        IFilePanelSource source, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        var slots = Slots.GetValue(source, static _ => new SemaphoreSlim(2, 2));
        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slots.Release();
        }
    }

    private static Task RunLegacyAsync(
        IFilePanelSource source, Func<Task> operation, CancellationToken cancellationToken) =>
        RunLegacyAsync(source, async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, cancellationToken);

    private sealed class LegacyAdapter(IFilePanelSource source) : IAsyncFilePanelSource
    {
        public PanelSourceId SourceId => source.SourceId;
        public string DisplayName => source.DisplayName;
        public PanelProviderCapabilities Capabilities => source.Capabilities;
        public IReadOnlyCollection<char> PathSeparators => source.PathSeparators;
        public StringComparer PathComparer => source.PathComparer;
        public string NormalizePath(string sourcePath) => source.NormalizePath(sourcePath);
        public string CombinePath(string parent, string name) => source.CombinePath(parent, name);
        public string GetFileName(string sourcePath) => source.GetFileName(sourcePath);
        public IReadOnlyList<string> GetRelativePathSegments(string root, string path) => source.GetRelativePathSegments(root, path);
        public bool IsRootPath(string sourcePath) => source.IsRootPath(sourcePath);
        public string? GetParentPath(string sourcePath) => source.GetParentPath(sourcePath);
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string sourcePath, CancellationToken cancellationToken = default) =>
            source.EnumerateDirectory(sourcePath, cancellationToken);
        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) =>
            source.GetItem(sourcePath, cancellationToken);

        public Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            RunBlockingAsync(source, () => source.EnumerateDirectory(sourcePath, cancellationToken), cancellationToken);
        public Task<FilePanelItem?> GetItemAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            RunBlockingAsync(source, () => source.GetItem(sourcePath, cancellationToken), cancellationToken);
        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            RunLegacyAsync(source, () => source.OpenReadAsync(sourcePath, cancellationToken), cancellationToken);
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default) =>
            RunLegacyAsync(source, () => source.OpenWriteAsync(sourcePath, overwrite, cancellationToken), cancellationToken);
        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            RunLegacyAsync(source, () => source.CreateDirectoryAsync(sourcePath, cancellationToken), cancellationToken);
        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) =>
            RunLegacyAsync(source, () => source.DeleteAsync(sourcePath, recursive, cancellationToken), cancellationToken);
        public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) =>
            RunLegacyAsync(source, () => source.RenameAsync(sourcePath, newSourcePath, cancellationToken), cancellationToken);
    }
}
