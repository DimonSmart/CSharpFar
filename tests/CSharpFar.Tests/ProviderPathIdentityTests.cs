using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Controllers;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class ProviderPathIdentityTests
{
    [Fact]
    public void LogicalIdentity_RespectsSourceAndNormalizedCase()
    {
        IFilePanelSource insensitive = new PathOnlySource("one", StringComparer.OrdinalIgnoreCase);
        IFilePanelSource sensitive = new PathOnlySource("two", StringComparer.Ordinal);
        var comparer = new PanelLocationComparer([insensitive, sensitive]);
        var first = new PanelLocation(insensitive.SourceId, "|Dir|File.txt|");
        var equivalent = new PanelLocation(insensitive.SourceId, "|dir|file.txt");
        Assert.Equal(comparer.GetHashCode(first), comparer.GetHashCode(equivalent));
        var set = new HashSet<PanelLocation>(comparer) { first, equivalent };
        Assert.Single(set);
        set.Add(new PanelLocation(sensitive.SourceId, "|Dir|File.txt"));
        set.Add(new PanelLocation(sensitive.SourceId, "|Dir|file.txt"));
        Assert.Equal(3, set.Count);
    }

    [Fact]
    public void ProviderCopyTarget_RejectsCaseEquivalentItemAndDescendant()
    {
        IFilePanelSource provider = new PathOnlySource("one", StringComparer.OrdinalIgnoreCase);
        Assert.Throws<IOException>(() => FileCopyPlanBuilder.ValidateCopyTarget(
            provider, "|Root|File", "|root|file", false));
        Assert.Throws<IOException>(() => FileCopyPlanBuilder.ValidateCopyTarget(
            provider, "|Root", "|root|Child", true));
        FileCopyPlanBuilder.ValidateCopyTarget(provider, "|Root", "|Root2|Child", true);
        Assert.Equal(new[] { "Child", "File" }, provider.GetRelativePathSegments("|ROOT", "|root|Child|File"));
        Assert.Equal("|Target|Child", provider.CombinePath("|Target|", "Child"));
    }

    [Fact]
    public async Task CustomProvider_CopyAndDeleteUseProviderQualifiedSelection()
    {
        var first = new PathOnlySource("first", StringComparer.Ordinal);
        var second = new PathOnlySource("second", StringComparer.OrdinalIgnoreCase);
        first.Seed("|File.txt", "one");
        first.Seed("|file.txt", "two");
        second.Seed("|File.txt", "other");
        await second.CreateDirectoryAsync("|target");
        var registry = new FilePanelSourceRegistry([first, second]);
        var controller = new PanelController(
            new PanelViewBuilder(new FileSystemService(), new PanelSortService(), sources: registry),
            sources: registry);
        var state = new FilePanelState();
        controller.ReplaceContent(state, new PanelContent(PanelLocation.SearchResult("|"),
            first.EnumerateDirectory("|").Concat(second.EnumerateDirectory("|").Where(item => !item.IsDirectory)),
            PanelProviderCapabilities.SearchResults), 20);
        controller.SelectRange(state, 0, state.Items.Count - 1);
        Assert.Equal(3, state.SelectedLocations.Count);
        Assert.Equal(11, state.Summary!.SelectedFileSize);
        controller.InvertSelection(state);
        Assert.Empty(state.SelectedLocations);
        controller.SelectRange(state, 0, state.Items.Count - 1);
        var selected = first.GetItem("|File.txt")!.Location;
        var service = new FileOperationService(registry);
        var copy = new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = [selected],
            Destination = new PanelLocation(second.SourceId, "|TARGET"),
            Options = new FileOperationOptions(),
        };
        Assert.Equal("|TARGET|File.txt", service.BuildPlan(copy).Items.Single().Destination.SourcePath, ignoreCase: true);
        var copied = await service.ExecuteAsync(copy, null, new NoConflict());
        Assert.Empty(copied.Errors);
        Assert.Equal("one", second.ReadText("|target|file.txt"));

        var deleted = await service.ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Delete,
            Sources = [selected],
            Options = new FileOperationOptions(),
        }, null, new NoConflict());
        Assert.Empty(deleted.Errors);
        Assert.Null(first.GetItem("|File.txt"));
        Assert.Equal("two", first.ReadText("|file.txt"));
        Assert.Equal("other", second.ReadText("|File.txt"));
    }

    [Fact]
    public async Task CustomProvider_RefreshAndCopyPreserveDistinctCaseVariants()
    {
        var source = new PathOnlySource("case-sensitive", StringComparer.Ordinal);
        source.Seed("|File.txt", "one");
        source.Seed("|file.txt", "two");
        await source.CreateDirectoryAsync("|target");
        var registry = new FilePanelSourceRegistry([source]);
        var controller = new PanelController(
            new PanelViewBuilder(new FileSystemService(), new PanelSortService(), sources: registry),
            sources: registry);
        var state = new FilePanelState();
        controller.LoadLocation(state, new PanelLocation(source.SourceId, "|"));
        var upper = source.GetItem("|File.txt")!.Location;
        var lower = source.GetItem("|file.txt")!.Location;
        Assert.True(controller.SetCursorByLocation(state, upper, 20));
        controller.ToggleCurrentSelection(state);
        Assert.True(controller.SetCursorByLocation(state, lower, 20));
        controller.ToggleCurrentSelection(state);
        controller.RefreshDirectory(state, 20);
        Assert.Contains(upper, state.SelectedLocations);
        Assert.Contains(lower, state.SelectedLocations);
        Assert.Equal(2, state.SelectedLocations.Count);
        controller.ToggleCurrentSelection(state);
        Assert.Contains(upper, state.SelectedLocations);
        Assert.DoesNotContain(lower, state.SelectedLocations);
        controller.ToggleCurrentSelection(state);
        var service = new FileOperationService(registry);
        var result = await service.ExecuteAsync(new FileOperationRequest
        {
            Kind = FileOperationKind.Copy,
            Sources = state.SelectedLocations.ToArray(),
            Destination = new PanelLocation(source.SourceId, "|target"),
            Options = new FileOperationOptions(),
        }, null, new NoConflict());
        Assert.Empty(result.Errors);
        Assert.Equal("one", source.ReadText("|target|File.txt"));
        Assert.Equal("two", source.ReadText("|target|file.txt"));
    }

    private sealed class NoConflict : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            throw new InvalidOperationException("Unexpected conflict.");
    }

    private sealed class PathOnlySource(string id, StringComparer comparer) : IFilePanelSource
    {
        public PanelSourceId SourceId { get; } = new(id);
        public string DisplayName => id;
        public PanelProviderCapabilities Capabilities => PanelProviderCapabilities.LocalFileSystem & ~PanelProviderCapabilities.Watch;
        private readonly Dictionary<string, byte[]?> _items = new(comparer) { ["|"] = null };
        public void Seed(string path, string text) => _items[NormalizePath(path)] = System.Text.Encoding.UTF8.GetBytes(text);
        public string? ReadText(string path) => _items.TryGetValue(NormalizePath(path), out var bytes) && bytes is not null ? System.Text.Encoding.UTF8.GetString(bytes) : null;
        public IReadOnlyCollection<char> PathSeparators => ['|'];
        public StringComparer PathComparer => comparer;
        public string NormalizePath(string sourcePath) =>
            "|" + string.Join("|", sourcePath.Split('|', StringSplitOptions.RemoveEmptyEntries));
        public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "|";
        public string? GetParentPath(string sourcePath)
        {
            string path = NormalizePath(sourcePath);
            if (path == "|") return null;
            int index = path.LastIndexOf('|');
            return index == 0 ? "|" : path[..index];
        }
        private FilePanelItem Item(string path, byte[]? bytes) => new()
        {
            SourceId = SourceId, FullPath = path, Name = path[(path.LastIndexOf('|') + 1)..],
            IsDirectory = bytes is null, Size = bytes?.Length,
            Attributes = bytes is null ? FileAttributes.Directory : FileAttributes.Normal,
        };
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string sourcePath, CancellationToken cancellationToken = default) =>
            _items.Where(pair => GetParentPath(pair.Key) is { } parent && comparer.Equals(parent, NormalizePath(sourcePath)))
                .Select(pair => Item(pair.Key, pair.Value)).ToArray();
        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) =>
            _items.TryGetValue(NormalizePath(sourcePath), out var bytes) ? Item(NormalizePath(sourcePath), bytes) : null;
        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(_items[NormalizePath(sourcePath)]!, writable: false));
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            string path = NormalizePath(sourcePath);
            if (!overwrite && _items.ContainsKey(path)) throw new IOException("Already exists.");
            return Task.FromResult<Stream>(new WriteStream(bytes => _items[path] = bytes));
        }
        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default)
        {
            _items[NormalizePath(sourcePath)] = null;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default)
        {
            _items.Remove(NormalizePath(sourcePath));
            return Task.CompletedTask;
        }
        public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default)
        {
            _items.Remove(NormalizePath(sourcePath), out var bytes);
            _items.Add(NormalizePath(newSourcePath), bytes);
            return Task.CompletedTask;
        }
        private sealed class WriteStream(Action<byte[]> commit) : MemoryStream
        {
            private bool _closed;
            protected override void Dispose(bool disposing)
            {
                if (disposing && !_closed) { _closed = true; commit(ToArray()); }
                base.Dispose(disposing);
            }
        }
    }
}
