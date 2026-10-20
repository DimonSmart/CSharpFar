using System.Text;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

using static CSharpFar.Tests.RemoteConnectionDialogTestFixture;

namespace CSharpFar.Tests;

public sealed partial class Spec029SftpProviderTests : IDisposable
{
    private readonly RemoteConnectionTestWorkspace _workspace = new("CSharpFar.Spec029.");
    private string _tempDir => _workspace.DirectoryPath;

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task FileOperationService_CopiesFromRemoteProviderToLocalDirectory()
    {
        Directory.CreateDirectory(_tempDir);
        var remote = new MemoryPanelSource(new PanelSourceId("fake-remote"));
        remote.WriteFile("/hello.txt", "hello from remote");
        var localFs = new FileSystemService();
        var registry = new FilePanelSourceRegistry(
        [
            remote,
            new LocalFilePanelSource(localFs),
        ]);
        var service = new FileOperationService(registry);

        var result = await service.ExecuteAsync(
            new FileOperationRequest
            {
                Kind = FileOperationKind.Copy,
                Sources = [],
                SourceLocations = [new PanelLocation(remote.SourceId, "/hello.txt")],
                Destination = _tempDir,
                DestinationLocation = PanelLocation.Local(_tempDir),
                Options = new FileOperationOptions
                {
                    DefaultConflictDecision = ConflictDecisionMode.Overwrite,
                },
            },
            progress: null,
            conflictResolver: new NoOpConflictResolver());

        Assert.Equal(1, result.CopiedCount);
        Assert.Equal("hello from remote", File.ReadAllText(Path.Combine(_tempDir, "hello.txt")));
    }

    [Fact]
    public async Task ProviderCopy_DoesNotAllowReliableMode()
    {
        var service = CreateProviderCopyService(out var remote);
        remote.WriteFile("/hello.txt", "hello from remote");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(
                CreateProviderCopyRequest(remote.SourceId, new FileOperationOptions { CopyMode = CopyMode.Reliable }),
                progress: null,
                conflictResolver: new NoOpConflictResolver()));

        Assert.Equal("Reliable copy is not supported for provider copy.", ex.Message);
    }

    [Fact]
    public async Task ProviderCopy_DoesNotAllowFastSalvageMode()
    {
        var service = CreateProviderCopyService(out var remote);
        remote.WriteFile("/hello.txt", "hello from remote");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(
                CreateProviderCopyRequest(remote.SourceId, new FileOperationOptions { CopyMode = CopyMode.FastSalvage }),
                progress: null,
                conflictResolver: new NoOpConflictResolver()));

        Assert.Equal("Fast salvage copy is not supported for provider copy.", ex.Message);
    }

    [Fact]
    public async Task ProviderCopy_DoesNotAllowOnlyNewer()
    {
        var service = CreateProviderCopyService(out var remote);
        remote.WriteFile("/hello.txt", "hello from remote");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(
                CreateProviderCopyRequest(
                    remote.SourceId,
                    new FileOperationOptions { DefaultConflictDecision = ConflictDecisionMode.OnlyNewer }),
                progress: null,
                conflictResolver: new NoOpConflictResolver()));

        Assert.Equal("Only newer is not supported for provider copy.", ex.Message);
    }

    [Fact]
    public async Task ProviderMove_FailedRenameDoesNotFallbackOrDeleteAnything()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/source.txt", "new");
        await remote.CreateDirectoryAsync("/Target");
        remote.RenameFailure = (from, to) =>
            from == "/source.txt" && to == "/Target/source.txt"
                ? new IOException("simulated provider rename failure")
                : null;

        await Assert.ThrowsAsync<IOException>(() =>
            service.ExecuteAsync(
                ProviderRequest(
                    FileOperationKind.Move,
                    remote.SourceId,
                    ["/source.txt"],
                    "/Target",
                    new FileOperationOptions()),
                null,
                new NoOpConflictResolver()));

        Assert.Equal("new", remote.ReadFile("/source.txt"));
        Assert.Null(remote.GetItem("/Target/source.txt"));
        Assert.Empty(remote.DeletedPaths);
    }

    [Fact]
    public async Task ProviderMove_OverwriteFailureRestoresDestinationWithoutDeleteFirst()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/source.txt", "new");
        remote.WriteFile("/Target/source.txt", "old");
        remote.RenameFailure = (from, to) =>
            from == "/source.txt" && to == "/Target/source.txt"
                ? new IOException("simulated provider commit failure")
                : null;

        await Assert.ThrowsAsync<IOException>(() =>
            service.ExecuteAsync(
                ProviderRequest(
                    FileOperationKind.Move,
                    remote.SourceId,
                    ["/source.txt"],
                    "/Target",
                    new FileOperationOptions { DefaultConflictDecision = ConflictDecisionMode.Overwrite }),
                null,
                new NoOpConflictResolver()));

        Assert.Equal("new", remote.ReadFile("/source.txt"));
        Assert.Equal("old", remote.ReadFile("/Target/source.txt"));
        Assert.Empty(remote.DeletedPaths);
        Assert.DoesNotContain(
            remote.EnumerateDirectory("/Target"),
            item => item.Name.Contains(".csharpfar-replace-backup-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProviderMove_FileMaskFiltersBeforeWildcardCollision()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/foo.txt", "text");
        remote.WriteFile("/foo.csv", "csv");
        await remote.CreateDirectoryAsync("/Moved");

        await service.ExecuteAsync(ProviderRequest(FileOperationKind.Move, remote.SourceId, ["/foo.txt", "/foo.csv"], "/Moved/*.bak", new FileOperationOptions { FileMask = "*.txt" }), null, new NoOpConflictResolver());

        Assert.Null(remote.GetItem("/foo.txt"));
        Assert.NotNull(remote.GetItem("/foo.csv"));
        Assert.NotNull(remote.GetItem("/Moved/foo.bak"));
    }

    [Fact]
    public async Task ProviderCopy_FileMaskFiltersBeforeWildcardCollision()
    {
        var service = CreateProviderOperationService(out var remote);
        remote.WriteFile("/foo.txt", "text");
        remote.WriteFile("/foo.csv", "csv");

        await service.ExecuteAsync(ProviderRequest(FileOperationKind.Copy, remote.SourceId, ["/foo.txt", "/foo.csv"], "/Moved/*.bak", new FileOperationOptions { FileMask = "*.txt" }), null, new NoOpConflictResolver());

        Assert.NotNull(remote.GetItem("/foo.txt"));
        Assert.NotNull(remote.GetItem("/foo.csv"));
        Assert.NotNull(remote.GetItem("/Moved/foo.bak"));
    }

    private static SftpConnectionInfo TestConnection() =>
        new()
        {
            Id = "conn1",
            DisplayName = "test",
            Host = "example.test",
            Port = 22,
            Username = "user",
            RemoteRootPath = "/",
            ExpectedHostKeyFingerprint = "AA:BB",
        };

    private FileOperationService CreateProviderCopyService(out MemoryPanelSource remote)
    {
        Directory.CreateDirectory(_tempDir);
        remote = new MemoryPanelSource(new PanelSourceId("fake-remote"));
        var localFs = new FileSystemService();
        var registry = new FilePanelSourceRegistry(
        [
            remote,
            new LocalFilePanelSource(localFs),
        ]);
        return new FileOperationService(registry);
    }

    private FileOperationService CreateProviderOperationService(out MemoryPanelSource remote)
    {
        remote = new MemoryPanelSource(new PanelSourceId("fake-remote"));
        var registry = new FilePanelSourceRegistry(
        [
            remote,
            new LocalFilePanelSource(new FileSystemService()),
        ]);
        return new FileOperationService(registry);
    }

    private static FileOperationRequest ProviderRequest(
        FileOperationKind kind,
        PanelSourceId sourceId,
        IReadOnlyList<string> sourcePaths,
        string destination,
        FileOperationOptions options,
        bool useDestinationTemplate = false) =>
        new()
        {
            Kind = kind,
            Sources = [],
            SourceLocations = sourcePaths.Select(path => new PanelLocation(sourceId, path)).ToList(),
            Destination = destination,
            DestinationLocation = new PanelLocation(sourceId, destination),
            UseDestinationTemplate = useDestinationTemplate,
            Options = options,
        };

    private FileOperationRequest CreateProviderCopyRequest(
        PanelSourceId remoteSourceId,
        FileOperationOptions options) =>
        new()
        {
            Kind = FileOperationKind.Copy,
            Sources = [],
            SourceLocations = [new PanelLocation(remoteSourceId, "/hello.txt")],
            Destination = _tempDir,
            DestinationLocation = PanelLocation.Local(_tempDir),
            Options = options,
        };

    private sealed class NoOpConflictResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private sealed class FixedConflictResolver(ConflictDecisionMode decision) : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(decision);
    }






    private sealed class MemoryPanelSource : IFilePanelSource
    {
        private static readonly char[] ProviderPathSeparators = ['/'];
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { "/" };

        public MemoryPanelSource(PanelSourceId sourceId)
        {
            SourceId = sourceId;
        }

        public PanelSourceId SourceId { get; }

        public string DisplayName => "Fake remote";

        public PanelProviderCapabilities Capabilities =>
            PanelProviderCapabilities.Enumerate |
            PanelProviderCapabilities.OpenRead |
            PanelProviderCapabilities.OpenWrite |
            PanelProviderCapabilities.CreateDirectory |
            PanelProviderCapabilities.Delete |
            PanelProviderCapabilities.Rename |
            PanelProviderCapabilities.CopyFrom |
            PanelProviderCapabilities.CopyTo |
            PanelProviderCapabilities.Refresh;

        public IReadOnlyCollection<char> PathSeparators => ProviderPathSeparators;

        public void WriteFile(string sourcePath, string text)
        {
            string path = NormalizePath(sourcePath);
            AddDirectoryAndParents(ParentPath(path));
            _files[path] = Encoding.UTF8.GetBytes(text);
        }

        public string ReadFile(string sourcePath) =>
            Encoding.UTF8.GetString(_files[NormalizePath(sourcePath)]);

        public List<string> DeletedPaths { get; } = [];

        public Func<string, string, Exception?>? RenameFailure { get; set; }

        public string NormalizePath(string sourcePath)
        {
            if (!sourcePath.StartsWith('/'))
                sourcePath = "/" + sourcePath;
            return sourcePath.Length > 1 ? sourcePath.TrimEnd('/') : "/";
        }

        public bool IsRootPath(string sourcePath) => NormalizePath(sourcePath) == "/";

        public string? GetParentPath(string sourcePath)
        {
            string path = NormalizePath(sourcePath);
            return path == "/" ? null : ParentPath(path);
        }

        public IReadOnlyList<FilePanelItem> EnumerateDirectory(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            string directory = NormalizePath(sourcePath);
            return _directories
                .Where(path => path != directory && ParentPath(path) == directory)
                .Select(ToDirectoryItem)
                .Concat(_files
                .Where(pair => ParentPath(pair.Key) == directory)
                .Select(pair => ToFileItem(pair.Key, pair.Value.Length)))
                .ToList();
        }

        public FilePanelItem? GetItem(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            string path = NormalizePath(sourcePath);
            if (_files.TryGetValue(path, out var bytes))
                return ToFileItem(path, bytes.Length);
            if (_directories.Contains(path))
            {
                return new FilePanelItem
                {
                    Name = path == "/" ? "/" : path[(path.LastIndexOf('/') + 1)..],
                    FullPath = path,
                    SourceId = SourceId,
                    IsDirectory = true,
                    Size = null,
                    LastWriteTime = DateTime.UnixEpoch,
                    Attributes = FileAttributes.Directory,
                    IsParentDirectory = false,
                };
            }

            return null;
        }

        public Task<Stream> OpenReadAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(_files[NormalizePath(sourcePath)], writable: false));

        public Task<Stream> OpenWriteAsync(
            string sourcePath,
            bool overwrite,
            CancellationToken cancellationToken = default)
        {
            string path = NormalizePath(sourcePath);
            var stream = new MemoryWriteStream(bytes => _files[path] = bytes);
            return Task.FromResult<Stream>(stream);
        }

        public Task CreateDirectoryAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            string path = NormalizePath(sourcePath);
            if (_directories.Contains(path))
                return Task.CompletedTask;
            if (!_directories.Contains(ParentPath(path)))
                throw new IOException($"Parent directory does not exist: {ParentPath(path)}");

            _directories.Add(path);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string sourcePath,
            bool recursive,
            CancellationToken cancellationToken = default)
        {
            string path = NormalizePath(sourcePath);
            DeletedPaths.Add(path);
            _files.Remove(path);
            if (recursive)
            {
                string prefix = path == "/" ? "/" : path + "/";
                foreach (string file in _files.Keys.Where(item => item.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                    _files.Remove(file);
                foreach (string directory in _directories.Where(item => item == path || item.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                    _directories.Remove(directory);
            }
            else
            {
                _directories.Remove(path);
            }

            return Task.CompletedTask;
        }

        public Task RenameAsync(
            string sourcePath,
            string newSourcePath,
            CancellationToken cancellationToken = default)
        {
            string source = NormalizePath(sourcePath);
            string target = NormalizePath(newSourcePath);
            if (RenameFailure?.Invoke(source, target) is { } failure)
                throw failure;

            if (_files.TryGetValue(source, out var bytes))
            {
                AddDirectoryAndParents(ParentPath(target));
                _files[target] = bytes;
                _files.Remove(source);
            }
            else if (_directories.Contains(source))
            {
                string prefix = source == "/" ? "/" : source + "/";
                foreach (string file in _files.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                {
                    string renamed = target + file[source.Length..];
                    _files[renamed] = _files[file];
                    _files.Remove(file);
                }
                foreach (string directory in _directories.Where(path => path == source || path.StartsWith(prefix, StringComparison.Ordinal)).OrderByDescending(path => path.Length).ToArray())
                {
                    _directories.Remove(directory);
                    _directories.Add(target + directory[source.Length..]);
                }
            }
            return Task.CompletedTask;
        }

        private FilePanelItem ToDirectoryItem(string path) =>
            new()
            {
                Name = path[(path.LastIndexOf('/') + 1)..],
                FullPath = path,
                SourceId = SourceId,
                IsDirectory = true,
                Size = null,
                LastWriteTime = DateTime.UnixEpoch,
                Attributes = FileAttributes.Directory,
                IsParentDirectory = false,
            };

        private FilePanelItem ToFileItem(string path, long size) =>
            new()
            {
                Name = path[(path.LastIndexOf('/') + 1)..],
                FullPath = path,
                SourceId = SourceId,
                IsDirectory = false,
                Size = size,
                LastWriteTime = DateTime.UnixEpoch,
                Attributes = FileAttributes.Normal,
                IsParentDirectory = false,
            };

        private static string ParentPath(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path[..slash];
        }

        private void AddDirectoryAndParents(string path)
        {
            while (_directories.Add(path) && path != "/")
                path = ParentPath(path);
        }
    }

    private sealed class MemoryWriteStream : MemoryStream
    {
        private readonly Action<byte[]> _commit;

        public MemoryWriteStream(Action<byte[]> commit)
        {
            _commit = commit;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _commit(ToArray());
            base.Dispose(disposing);
        }
    }
}
