using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.Tests.Fakes;

/// <summary>
/// In-memory IFileSystemService for unit tests.
/// Register directories with AddDirectory; ReadDirectory returns registered items.
/// </summary>
public sealed class FakeFileSystemService : IFileSystemService
{
    private readonly Dictionary<string, List<FilePanelItem>> _dirs =
        new(StringComparer.OrdinalIgnoreCase);

    public void AddDirectory(string path, params FilePanelItem[] items) =>
        _dirs[path] = [.. items];

    public IReadOnlyList<FilePanelItem> ReadDirectory(string path) =>
        _dirs.TryGetValue(path, out var items) ? items : [];

    public bool DirectoryExists(string path) => _dirs.ContainsKey(path);
    public bool FileExists(string path) => false;
}
