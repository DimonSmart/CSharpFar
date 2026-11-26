using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Plugin.AzureBlob;

public sealed class AzureBlobFilePanelSource(BlobContainerClient container, string displayName, bool readOnly, int? listingPageSize = null)
    : IAsyncFilePanelSource, ISafeNewFilePublicationProvider, IFileProviderPhysicalIdentity, ITemporaryFilePanelSource
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> stages = new(StringComparer.Ordinal);
    private const string StageKey = "csharpfar_stage";
    public bool IsTemporary => true;
    private const string MarkerName = ".csharpfar-directory";
    private const string MarkerKey = "csharpfar_directory";
    private const string MarkerValue = "azure-blob-v1";
    private static BlobRequestConditions Exclusive => new() { IfNoneMatch = ETag.All };
    public PanelSourceId SourceId { get; } = PanelSourceId.Module(new Guid("67f4cdd6-53bb-4406-8ae1-a42380395ac7"), Guid.NewGuid().ToString("N"));
    public string DisplayName => displayName;
    public string PhysicalIdentity => "azure-blob:" + container.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.Enumerate | PanelProviderCapabilities.OpenRead |
        PanelProviderCapabilities.CopyFrom | PanelProviderCapabilities.Refresh |
        (readOnly ? PanelProviderCapabilities.None : PanelProviderCapabilities.OpenWrite | PanelProviderCapabilities.CreateFile |
        PanelProviderCapabilities.CreateDirectory | PanelProviderCapabilities.Delete | PanelProviderCapabilities.Rename |
        PanelProviderCapabilities.CopyTo | PanelProviderCapabilities.MoveFrom | PanelProviderCapabilities.MoveTo);
    public IReadOnlyCollection<char> PathSeparators { get; } = ['/'];
    public StringComparer PathComparer => StringComparer.Ordinal;
    public string NormalizePath(string path) => AzureBlobPath.Normalize(path);
    public string CombinePath(string parent, string name) => AzureBlobPath.Combine(parent, name);
    public string GetFileName(string path) => AzureBlobPath.Name(path);
    public bool IsRootPath(string path) => NormalizePath(path) == "/";
    public string? GetParentPath(string path) => AzureBlobPath.Parent(path);

    private void Writable()
    {
        if (readOnly) throw new AzureOperationException("Azure connection is read-only.");
    }

    private static bool IsMarker(string name, long? size, IDictionary<string, string>? metadata)
        => name.EndsWith("/" + MarkerName, StringComparison.Ordinal) && size == 0 && metadata is not null &&
            metadata.TryGetValue(MarkerKey, out var value) && value == MarkerValue;
    private static string SafeName(string blobName)
    {
        // A leading slash is a real blob-name character, not the panel's root.
        if (blobName.StartsWith('/')) throw new AzureOperationException("Leading-slash blob names are not supported safely.");
        try { return AzureBlobPath.Normalize("/" + blobName); }
        catch (IOException) { throw new AzureOperationException("A blob name cannot be represented safely."); }
    }

    private FilePanelItem Item(string path, bool directory, long? size = null, DateTimeOffset? modified = null)
        => new() { Name = GetFileName(path), FullPath = path, SourceId = SourceId, IsDirectory = directory,
            Size = directory ? null : size, LastWriteTime = modified?.UtcDateTime ?? DateTime.MinValue,
            Attributes = (directory ? FileAttributes.Directory : FileAttributes.Normal) |
                (readOnly ? FileAttributes.ReadOnly : 0) };

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(string path, CancellationToken token = default)
        => EnumerateDirectoryAsync(path, token).GetAwaiter().GetResult();
    public FilePanelItem? GetItem(string path, CancellationToken token = default)
        => GetItemAsync(path, token).GetAwaiter().GetResult();

    public async Task<IReadOnlyList<FilePanelItem>> EnumerateDirectoryAsync(string path, CancellationToken token = default)
    {
        try
        {
            string prefix = AzureBlobPath.Prefix(path);
            var items = new List<FilePanelItem>();
            await foreach (var page in container.GetBlobsByHierarchyAsync(BlobTraits.Metadata, BlobStates.None,
                delimiter: "/", prefix: prefix, cancellationToken: token).AsPages(pageSizeHint: listingPageSize).ConfigureAwait(false))
            foreach (var entry in page.Values)
            {
                if (entry.IsPrefix)
                    items.Add(Item(SafeName(entry.Prefix), true));
                else if (entry.IsBlob)
                {
                    var blob = entry.Blob;
                    if (IsMarker(blob.Name, blob.Properties.ContentLength, blob.Metadata)) continue;
                    if (blob.Name.EndsWith('/'))
                        throw new AzureOperationException("A user blob ending in slash cannot be represented safely.");
                    items.Add(Item(SafeName(blob.Name), false, blob.Properties.ContentLength, blob.Properties.LastModified));
                }
            }
            // A file and directory with the same display name cannot be safely targeted
            // by all host operations; refuse this directory rather than act on the wrong blob.
            if (items.GroupBy(item => item.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new AzureOperationException("A file and virtual directory have the same name; operations are unsafe.");
            return items;
        }
        catch (Exception error) { throw AzureErrors.Safe(error); }
    }

    public async Task<FilePanelItem?> GetItemAsync(string path, CancellationToken token = default)
    {
        try
        {
            path = NormalizePath(path);
            if (path == "/") return Item(path, true);
            string file = path.TrimEnd('/')[1..];
            BlobProperties? properties = null;
            try { properties = (await container.GetBlobClient(file).GetPropertiesAsync(cancellationToken: token).ConfigureAwait(false)).Value; }
            catch (RequestFailedException error) when (error.Status == 404) { }
            string prefix = AzureBlobPath.Prefix(path);
            bool directory = false;
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, prefix: prefix,
                cancellationToken: token).ConfigureAwait(false))
            {
                if (blob.Name == prefix)
                    throw new AzureOperationException("A user blob ending in slash cannot be represented safely.");
                directory = true;
                break;
            }
            if (properties is not null && IsMarker(file, properties.ContentLength, properties.Metadata))
                throw new AzureOperationException("The requested name is a directory marker.");
            if (properties is not null && directory)
                throw new AzureOperationException("A file and virtual directory have the same name; operations are unsafe.");
            if (path.EndsWith('/') && !directory) return null;
            if (properties is not null) return Item("/" + file, false, properties.ContentLength, properties.LastModified);
            return directory ? Item("/" + prefix, true) : null;
        }
        catch (Exception error) { throw AzureErrors.Safe(error); }
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken token = default)
    {
        try
        {
            string name = AzureBlobPath.BlobName(path);
            var stream = await container.GetBlobClient(name).OpenReadAsync(
                new BlobOpenReadOptions(allowModifications: false), token).ConfigureAwait(false);
            return new AzureSafeStream(stream);
        }
        catch (Exception error) { throw AzureErrors.Safe(error); }
    }

    public Task<Stream> OpenWriteAsync(string path, bool overwrite, CancellationToken token = default)
        => overwrite ? Task.FromException<Stream>(new AzureOperationException("Overwriting existing blobs is not supported."))
            : OpenExclusiveWriteAsync(path, false, token);

    public Task<Stream> OpenStagingWriteAsync(string path, CancellationToken token = default)
        => OpenExclusiveWriteAsync(path, true, token);

    private async Task<Stream> OpenExclusiveWriteAsync(string path, bool staging, CancellationToken token)
    {
        Writable();
        try
        {
            string name = AzureBlobPath.BlobName(path);
            if (await GetItemAsync(path, token).ConfigureAwait(false) is not null)
                throw new AzureOperationException("The destination already exists.");
            // The SDK's overwrite flag must be true, but OpenConditions are applied
            // to its initial empty upload. All subsequent commits use its returned ETag.
            string? owner = staging ? Guid.NewGuid().ToString("N") : null;
            var stream = await container.GetBlockBlobClient(name).OpenWriteAsync(true,
                new BlockBlobOpenWriteOptions { OpenConditions = Exclusive, Metadata = owner is null ? null : new Dictionary<string, string> { [StageKey] = owner } }, token).ConfigureAwait(false);
            if (owner is not null) stages[name] = owner;
            return new AzureSafeStream(stream);
        }
        catch (Exception error) { throw AzureErrors.Safe(error); }
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken token = default)
    {
        Writable();
        try
        {
            if (IsRootPath(path)) return;
            var existing = await GetItemAsync(path.TrimEnd('/'), token).ConfigureAwait(false);
            if (existing?.IsDirectory == true) return;
            if (existing is not null) throw new AzureOperationException("A file already has this name.");
            string prefix = AzureBlobPath.Prefix(path);
            await container.GetBlobClient(prefix + MarkerName).UploadAsync(Stream.Null,
                new BlobUploadOptions { Conditions = Exclusive, Metadata = new Dictionary<string, string> { [MarkerKey] = MarkerValue } },
                token).ConfigureAwait(false);
        }
        catch (Exception error) { throw AzureErrors.Safe(error); }
    }

    public async Task DeleteAsync(string path, bool recursive, CancellationToken token = default)
    {
        Writable();
        try
        {
            if (IsRootPath(path)) throw new AzureOperationException("Deleting the container root is forbidden.");
            var item = await GetItemAsync(path, token).ConfigureAwait(false);
            if (item is null) return;
            if (!item.IsDirectory)
            {
                var blob = container.GetBlobClient(AzureBlobPath.BlobName(item.FullPath));
                var properties = (await blob.GetPropertiesAsync(cancellationToken: token).ConfigureAwait(false)).Value;
                string name = AzureBlobPath.BlobName(item.FullPath);
                if (stages.TryGetValue(name, out var owner) &&
                    (!properties.Metadata.TryGetValue(StageKey, out var actualOwner) || owner != actualOwner))
                    throw new AzureOperationException("Staging ownership changed; object was not deleted.");
                await blob.DeleteAsync(conditions: new BlobRequestConditions { IfMatch = properties.ETag }, cancellationToken: token).ConfigureAwait(false);
                stages.TryRemove(name, out _);
                return;
            }
            string prefix = AzureBlobPath.Prefix(item.FullPath);
            var entries = new List<BlobItem>();
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, prefix: prefix,
                cancellationToken: token).ConfigureAwait(false))
            {
                if (blob.Name.EndsWith('/'))
                    throw new AzureOperationException("A user blob ending in slash cannot be deleted through a directory.");
                _ = SafeName(blob.Name);
                if (!recursive && !(blob.Name == prefix + MarkerName && IsMarker(blob.Name, blob.Properties.ContentLength, blob.Metadata))) throw new AzureOperationException("Directory is not empty.");
                entries.Add(blob);
            }
            int deleted = 0;
            try
            {
                foreach (var blob in entries)
                {
                    await container.GetBlobClient(blob.Name).DeleteAsync(
                        conditions: new BlobRequestConditions { IfMatch = blob.Properties.ETag ?? throw new IOException("Missing ETag.") },
                        cancellationToken: token).ConfigureAwait(false);
                    deleted++;
                }
            }
            catch (Exception) when (deleted > 0)
            { throw new AzureOperationException($"Directory deletion may be partial ({deleted} acknowledged). Refresh before retrying."); }
        }
        catch (Exception error) { throw AzureErrors.Safe(error); }
    }

    public Task RenameAsync(string source, string destination, CancellationToken token = default)
        => RenameWithoutOverwriteAsync(source, destination, token);

    public async Task RenameWithoutOverwriteAsync(string source, string destination, CancellationToken token = default)
    {
        Writable();
        try
        {
            var item = await GetItemAsync(source, token).ConfigureAwait(false)
                ?? throw new AzureOperationException("Source does not exist.");
            if (await GetItemAsync(destination.TrimEnd('/'), token).ConfigureAwait(false) is not null)
                throw new AzureOperationException("Destination already exists.");
            if (!item.IsDirectory)
            {
                await CopyThenDelete(AzureBlobPath.BlobName(source), AzureBlobPath.BlobName(destination), token).ConfigureAwait(false);
                return;
            }
            string from = AzureBlobPath.Prefix(source), to = AzureBlobPath.Prefix(destination);
            if (from.Length == 0 || to.Length == 0 || to.StartsWith(from, StringComparison.Ordinal))
                throw new AzureOperationException("Invalid directory rename.");
            var names = new List<string>();
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, prefix: from, cancellationToken: token).ConfigureAwait(false))
            {
                if (blob.Name.EndsWith('/'))
                    throw new AzureOperationException("A user blob ending in slash cannot be renamed safely.");
                _ = SafeName(blob.Name);
                _ = SafeName(to + blob.Name[from.Length..]);
                names.Add(blob.Name);
            }
            int moved = 0;
            try
            {
                foreach (string name in names)
                {
                    await CopyThenDelete(name, to + name[from.Length..], token).ConfigureAwait(false);
                    moved++;
                }
            }
            catch (Exception) when (moved > 0)
            { throw new AzureOperationException($"Directory rename may be partial ({moved} acknowledged). Refresh before retrying."); }
        }
        catch (Exception error) { throw AzureErrors.Safe(error); }
    }

    private async Task CopyThenDelete(string from, string to, CancellationToken token)
    {
        var source = container.GetBlobClient(from);
        var destination = container.GetBlobClient(to);
        var properties = (await source.GetPropertiesAsync(cancellationToken: token).ConfigureAwait(false)).Value;
        var metadata = new Dictionary<string, string>(properties.Metadata, StringComparer.OrdinalIgnoreCase);
        if (stages.TryGetValue(from, out var owner))
        {
            if (!metadata.TryGetValue(StageKey, out var actualOwner) || actualOwner != owner)
                throw new AzureOperationException("Staging ownership changed; source retained.");
            metadata.Remove(StageKey);
        }
        await using var input = (await source.DownloadStreamingAsync(new BlobDownloadOptions
        {
            Conditions = new BlobRequestConditions { IfMatch = properties.ETag }
        }, token).ConfigureAwait(false)).Value.Content;
        // Stream through Azure SDK so this also works with private containers and
        // Azure CLI credentials; destination publication is conditional on absence.
        try
        {
            await destination.UploadAsync(input, new BlobUploadOptions
            {
                Conditions = Exclusive, Metadata = metadata,
                HttpHeaders = new BlobHttpHeaders { ContentType = properties.ContentType,
                    ContentEncoding = properties.ContentEncoding, ContentLanguage = properties.ContentLanguage,
                    CacheControl = properties.CacheControl, ContentDisposition = properties.ContentDisposition }
            }, token).ConfigureAwait(false);
        }
        catch (RequestFailedException error) when (error.Status is 409 or 412)
        { throw new AzureOperationException("Destination conflict or source changed. Source retained; refresh before retrying."); }
        catch (Exception)
        { throw new AzureOperationException("Publication outcome may be unknown. Source retained; inspect both names before retrying."); }
        try
        {
            await source.DeleteAsync(conditions: new BlobRequestConditions { IfMatch = properties.ETag },
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            stages.TryRemove(from, out _);
        }
        catch (Exception)
        { throw new AzureOperationException("Destination published; source deletion is unconfirmed. Inspect both names before retrying."); }
    }
}
