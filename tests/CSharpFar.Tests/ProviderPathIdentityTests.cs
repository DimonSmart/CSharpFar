using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
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

    private sealed class PathOnlySource(string id, StringComparer comparer) : IFilePanelSource
    {
        public PanelSourceId SourceId { get; } = new(id);
        public string DisplayName => id;
        public PanelProviderCapabilities Capabilities => default;
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
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
