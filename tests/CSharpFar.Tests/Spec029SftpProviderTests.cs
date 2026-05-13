using System.Text;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;

namespace CSharpFar.Tests;

public sealed class Spec029SftpProviderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "CSharpFar.Spec029." + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

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
    public void PanelLocation_SelectionKeyIncludesSourceId()
    {
        var left = new PanelLocation(new PanelSourceId("left"), "/same/path.txt");
        var right = new PanelLocation(new PanelSourceId("right"), "/same/path.txt");

        Assert.NotEqual(left.SelectionKey, right.SelectionKey);
    }

    [Fact]
    public void SftpConnectionStore_DoesNotWritePlaintextPasswordToMetadata()
    {
        Directory.CreateDirectory(_tempDir);
        var store = new SftpConnectionStore(_tempDir);

        store.Save(
        [
            new SftpConnectionInfo
            {
                Id = "conn1",
                DisplayName = "test",
                Host = "example.test",
                Port = 22,
                Username = "user",
                RemoteRootPath = "/",
                CredentialId = "cred1",
                ExpectedHostKeyFingerprint = "AA:BB",
                ShowInDriveSelection = false,
            },
        ]);

        string metadata = File.ReadAllText(Path.Combine(_tempDir, "connections.json"));
        Assert.DoesNotContain("secret-password", metadata, StringComparison.Ordinal);
        Assert.Contains("\"CredentialId\"", metadata, StringComparison.Ordinal);
        Assert.Contains("\"ShowInDriveSelection\": false", metadata, StringComparison.Ordinal);
        Assert.False(store.Load().Single().ShowInDriveSelection);
    }

    private sealed class NoOpConflictResolver : IFileOperationConflictResolver
    {
        public FileOperationConflictDecision Resolve(FileOperationConflict conflict) =>
            FileOperationConflictDecision.FromMode(ConflictDecisionMode.Overwrite);
    }

    private sealed class MemoryPanelSource : IFilePanelSource
    {
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

        public void WriteFile(string sourcePath, string text)
        {
            string path = NormalizePath(sourcePath);
            _directories.Add(ParentPath(path));
            _files[path] = Encoding.UTF8.GetBytes(text);
        }

        public string NormalizePath(string sourcePath)
        {
            sourcePath = sourcePath.Replace('\\', '/');
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
            return _files
                .Where(pair => ParentPath(pair.Key) == directory)
                .Select(pair => ToFileItem(pair.Key, pair.Value.Length))
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
            _directories.Add(NormalizePath(sourcePath));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string sourcePath,
            bool recursive,
            CancellationToken cancellationToken = default)
        {
            _files.Remove(NormalizePath(sourcePath));
            return Task.CompletedTask;
        }

        public Task RenameAsync(
            string sourcePath,
            string newSourcePath,
            CancellationToken cancellationToken = default)
        {
            string source = NormalizePath(sourcePath);
            string target = NormalizePath(newSourcePath);
            _files[target] = _files[source];
            _files.Remove(source);
            return Task.CompletedTask;
        }

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
