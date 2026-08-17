using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;

namespace CSharpFar.FileSystem;

public sealed class DemoFilePanelSource : IFilePanelSource
{
    private static readonly char[] ProviderPathSeparators = ['/'];
    private const int MaxFileCount = 512;
    private const long MaxSingleFileSizeBytes = 4 * 1024 * 1024;
    private const long MaxTotalContentSizeBytes = 16 * 1024 * 1024;

    private readonly DemoDirectoryNode _root;
    private readonly object _gate = new();

    private DemoFilePanelSource(DemoDirectoryNode root)
    {
        _root = root;
    }

    public PanelSourceId SourceId => PanelSourceId.Demo;

    public string DisplayName => "Demo";

    public PanelProviderCapabilities Capabilities =>
        PanelProviderCapabilities.Enumerate |
        PanelProviderCapabilities.OpenRead |
        PanelProviderCapabilities.OpenWrite |
        PanelProviderCapabilities.CreateFile |
        PanelProviderCapabilities.CreateDirectory |
        PanelProviderCapabilities.Delete |
        PanelProviderCapabilities.Rename |
        PanelProviderCapabilities.CopyFrom |
        PanelProviderCapabilities.CopyTo |
        PanelProviderCapabilities.MoveFrom |
        PanelProviderCapabilities.MoveTo |
        PanelProviderCapabilities.Edit |
        PanelProviderCapabilities.Refresh;

    public IReadOnlyCollection<char> PathSeparators => ProviderPathSeparators;

    public static DemoFilePanelSource ImportFromDirectory(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Demo fixture directory path is required.", nameof(rootPath));

        string fullPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Demo fixture directory does not exist: {fullPath}");

        var limits = new DemoImportLimits();
        var root = new DemoDirectoryNode(string.Empty, DateTime.UnixEpoch, FileAttributes.Directory)
        {
            Path = "/",
        };
        ImportDirectory(fullPath, root, limits);
        return new DemoFilePanelSource(root);
    }

    public string NormalizePath(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return "/";

        string[] parts = sourcePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalized = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            if (part == ".")
                continue;

            if (part == "..")
            {
                if (normalized.Count > 0)
                    normalized.RemoveAt(normalized.Count - 1);
                continue;
            }

            normalized.Add(part);
        }

        return normalized.Count == 0 ? "/" : "/" + string.Join('/', normalized);
    }

    public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "/";

    public string? GetParentPath(string sourcePath)
    {
        string normalized = NormalizePath(sourcePath);
        if (normalized == "/")
            return null;

        int slash = normalized.LastIndexOf('/');
        return slash <= 0 ? "/" : normalized[..slash];
    }

    public IReadOnlyList<FilePanelItem> EnumerateDirectory(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var directory = GetDirectoryNode(sourcePath);
            return directory.Children.Values
                .Select(ToItem)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public FilePanelItem? GetItem(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var node = TryGetNode(sourcePath);
            return node is null ? null : ToItem(node);
        }
    }

    public Task<Stream> OpenReadAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var file = GetFileNode(sourcePath);
            Stream stream = new MemoryStream(file.Content, writable: false);
            return Task.FromResult(stream);
        }
    }

    public Task<Stream> OpenWriteAsync(
        string sourcePath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            string normalized = NormalizePath(sourcePath);
            if (!overwrite && TryGetNode(normalized) is not null)
                throw new IOException($"File already exists: {normalized}");

            string? parentPath = GetParentPath(normalized);
            var parent = GetDirectoryNode(parentPath ?? "/");
            string name = GetName(normalized);
            return Task.FromResult<Stream>(new DemoWriteStream(bytes =>
            {
                lock (_gate)
                {
                    parent.Children[name] = new DemoFileNode(
                        name,
                        bytes,
                        DateTime.UnixEpoch,
                        FileAttributes.Normal)
                    {
                        Path = GetChildPath(parent.Path, name),
                    };
                }
            }));
        }
    }

    public Task CreateDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            string normalized = NormalizePath(sourcePath);
            if (normalized == "/")
                return Task.CompletedTask;

            EnsureDirectory(normalized);
            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(
        string sourcePath,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            string normalized = NormalizePath(sourcePath);
            if (normalized == "/")
                throw new IOException("Cannot delete the demo root directory.");

            var node = TryGetNode(normalized) ?? throw new FileNotFoundException("Demo item not found.", normalized);
            if (node is DemoDirectoryNode directory && directory.Children.Count > 0 && !recursive)
                throw new IOException("Directory is not empty.");

            var parent = GetDirectoryNode(GetParentPath(normalized) ?? "/");
            parent.Children.Remove(node.Name);
            return Task.CompletedTask;
        }
    }

    public Task RenameAsync(
        string sourcePath,
        string newSourcePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            string source = NormalizePath(sourcePath);
            string target = NormalizePath(newSourcePath);
            if (source == "/")
                throw new IOException("Cannot rename the demo root directory.");

            var node = TryGetNode(source) ?? throw new FileNotFoundException("Demo item not found.", source);
            if (ProviderPathRelations.PathsEqual(this, source, target))
                return Task.CompletedTask;

            if (node is DemoDirectoryNode &&
                ProviderPathRelations.IsDescendantOf(this, target, source))
            {
                throw new IOException("Cannot move a directory into itself.");
            }

            var sourceParent = GetDirectoryNode(GetParentPath(source) ?? "/");
            var targetParent = EnsureDirectory(GetParentPath(target) ?? "/");
            string targetName = GetName(target);
            if (targetParent.Children.ContainsKey(targetName))
                throw new IOException($"Target already exists: {target}");

            sourceParent.Children.Remove(node.Name);
            targetParent.Children[targetName] = node.Rename(targetName, GetChildPath(targetParent.Path, targetName));
            return Task.CompletedTask;
        }
    }

    private static void ImportDirectory(string physicalPath, DemoDirectoryNode target, DemoImportLimits limits)
    {
        foreach (string directoryPath in Directory.EnumerateDirectories(physicalPath))
        {
            var info = new DirectoryInfo(directoryPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            limits.RegisterNode();
            var child = new DemoDirectoryNode(info.Name, DateTime.UnixEpoch, FileAttributes.Directory)
            {
                Path = GetChildPath(target.Path, info.Name),
            };
            target.Children[info.Name] = child;
            ImportDirectory(info.FullName, child, limits);
        }

        foreach (string filePath in Directory.EnumerateFiles(physicalPath))
        {
            var info = new FileInfo(filePath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            if (info.Length > MaxSingleFileSizeBytes)
                throw new IOException($"Demo fixture file is too large: {info.FullName}");

            byte[] content = File.ReadAllBytes(info.FullName);
            limits.RegisterFile(content.LongLength);
            target.Children[info.Name] = new DemoFileNode(
                info.Name,
                content,
                DateTime.UnixEpoch,
                FileAttributes.Normal)
            {
                Path = GetChildPath(target.Path, info.Name),
            };
        }
    }

    private DemoDirectoryNode EnsureDirectory(string sourcePath)
    {
        string normalized = NormalizePath(sourcePath);
        var current = _root;
        foreach (string segment in GetSegments(normalized))
        {
            if (!current.Children.TryGetValue(segment, out DemoNode? child))
            {
                var created = new DemoDirectoryNode(segment, DateTime.UnixEpoch, FileAttributes.Directory)
                {
                    Path = GetChildPath(current.Path, segment),
                };
                current.Children[segment] = created;
                current = created;
                continue;
            }

            if (child is not DemoDirectoryNode directory)
                throw new IOException($"Path segment is not a directory: {segment}");

            current = directory;
        }

        return current;
    }

    private DemoDirectoryNode GetDirectoryNode(string sourcePath)
    {
        var node = TryGetNode(sourcePath);
        return node as DemoDirectoryNode
               ?? throw new DirectoryNotFoundException($"Demo directory not found: {NormalizePath(sourcePath)}");
    }

    private DemoFileNode GetFileNode(string sourcePath)
    {
        var node = TryGetNode(sourcePath);
        return node as DemoFileNode
               ?? throw new FileNotFoundException("Demo file not found.", NormalizePath(sourcePath));
    }

    private DemoNode? TryGetNode(string sourcePath)
    {
        string normalized = NormalizePath(sourcePath);
        if (normalized == "/")
            return _root;

        DemoNode current = _root;
        foreach (string segment in GetSegments(normalized))
        {
            if (current is not DemoDirectoryNode directory ||
                !directory.Children.TryGetValue(segment, out current!))
            {
                return null;
            }
        }

        return current;
    }

    private static IEnumerable<string> GetSegments(string sourcePath) =>
        sourcePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string GetName(string sourcePath)
    {
        string normalized = sourcePath.TrimEnd('/');
        int slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static string GetChildPath(string parentPath, string name) =>
        parentPath == "/" ? "/" + name : parentPath + "/" + name;

    private FilePanelItem ToItem(DemoNode node) =>
        new()
        {
            Name = node == _root ? "/" : node.Name,
            FullPath = node == _root ? "/" : node.Path,
            SourceId = SourceId,
            IsDirectory = node is DemoDirectoryNode,
            Size = node is DemoFileNode file ? file.Content.LongLength : null,
            LastWriteTime = node.LastWriteTime,
            Attributes = node.Attributes,
            IsParentDirectory = false,
        };

    private abstract record DemoNode(string Name, DateTime LastWriteTime, FileAttributes Attributes)
    {
        public string Path { get; set; } = "/";
        public abstract DemoNode Rename(string newName, string newPath);
    }

    private sealed record DemoDirectoryNode(string DirectoryName, DateTime LastWriteTime, FileAttributes Attributes)
        : DemoNode(DirectoryName, LastWriteTime, Attributes)
    {
        public Dictionary<string, DemoNode> Children { get; } = new(StringComparer.Ordinal);

        public override DemoNode Rename(string newName, string newPath)
        {
            var renamed = new DemoDirectoryNode(newName, LastWriteTime, Attributes)
            {
                Path = newPath,
            };
            foreach (var child in Children)
                renamed.Children[child.Key] = child.Value.Rename(child.Value.Name, GetChildPath(newPath, child.Value.Name));
            return renamed;
        }
    }

    private sealed record DemoFileNode(string FileName, byte[] Content, DateTime LastWriteTime, FileAttributes Attributes)
        : DemoNode(FileName, LastWriteTime, Attributes)
    {
        public override DemoNode Rename(string newName, string newPath) =>
            new DemoFileNode(newName, Content, LastWriteTime, Attributes)
            {
                Path = newPath,
            };
    }

    private sealed class DemoImportLimits
    {
        private int _fileCount;
        private long _totalBytes;

        public void RegisterNode()
        {
            _fileCount++;
            if (_fileCount > MaxFileCount)
                throw new IOException("Demo fixture contains too many files or directories.");
        }

        public void RegisterFile(long bytes)
        {
            RegisterNode();
            _totalBytes += bytes;
            if (_totalBytes > MaxTotalContentSizeBytes)
                throw new IOException("Demo fixture is too large for demo mode.");
        }
    }

    private sealed class DemoWriteStream(Action<byte[]> commit) : MemoryStream
    {
        private readonly Action<byte[]> _commit = commit;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _commit(ToArray());
            base.Dispose(disposing);
        }
    }
}
