using System.Text;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.FileSystem;
using CSharpFar.Tests.Fakes;

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

    [Fact]
    public void SftpConnectionDialog_ShowsCursorInFocusedInputField()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        bool cursorWasVisibleBeforeInput = false;
        driver.BeforeReadInput = currentDriver =>
        {
            cursorWasVisibleBeforeInput = currentDriver.CursorVisible;
            Assert.True(currentDriver.CursorX > 0);
            Assert.True(currentDriver.CursorY > 0);
        };
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new SftpConnectionDialog(screen).Show(
            new SftpConnectionDialogRequest(
                Connection: null,
                SavedPassword: null,
                SaveConnectionByDefault: false,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.True(cursorWasVisibleBeforeInput);
        Assert.Contains(driver.WriteRecords, r =>
            r.Text.Contains("Host:", StringComparison.Ordinal) &&
            r.Background == ConsoleColor.Gray);
    }

    [Fact]
    public void SftpConnectionDialog_MouseClickFocusesTextField()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        int readCount = 0;
        bool hostFieldFocused = false;
        Action<FakeConsoleDriver>? beforeRead = null;
        beforeRead = currentDriver =>
        {
            readCount++;
            if (readCount == 2)
            {
                hostFieldFocused =
                    currentDriver.CursorVisible &&
                    currentDriver.CursorX == 40 &&
                    currentDriver.CursorY == 9;
            }

            if (readCount < 2)
                currentDriver.BeforeReadInput = beforeRead;
        };
        driver.BeforeReadInput = beforeRead;
        driver.EnqueueInput(LeftMouse(40, 9));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new SftpConnectionDialog(screen).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: false,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.True(hostFieldFocused);
    }

    [Fact]
    public void SftpConnectionDialog_MouseClickTogglesCheckboxField()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(LeftMouse(17, 16));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new SftpConnectionDialog(screen).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.False(result.Connection.ShowInDriveSelection);
    }

    [Fact]
    public void SftpConnectionDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SftpConnectionDialog(screen).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.NotNull(result);
        Assert.Equal("test", result.Connection.DisplayName);
    }

    [Fact]
    public void SftpConnectionDialog_ShortConsoleRendersBodyScrollbar()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 12);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        var result = new SftpConnectionDialog(screen).Show(
            new SftpConnectionDialogRequest(
                TestConnection(),
                SavedPassword: "secret-password",
                SaveConnectionByDefault: true,
                AllowTemporaryConnection: true),
            _ => SftpConnectionDialogValidationResult.Accepted());

        Assert.Null(result);
        Assert.Contains(driver.WriteRecords, r => r.Text == "▲");
        Assert.Contains(driver.WriteRecords, r => r.Text == "▼");
    }

    [Fact]
    public void SftpConnectionManagerDialog_NewButtonSupportsMouseClick()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.BeforeReadInput = currentDriver =>
        {
            var button = currentDriver.WriteRecords.Last(r => r.Text.Contains("New", StringComparison.Ordinal));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(button.X + 1, button.Y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        };

        var result = new SftpConnectionManagerDialog(screen).Show([]);

        Assert.NotNull(result);
        Assert.Equal(SftpConnectionManagerAction.Create, result.Action);
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Connect", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Edit", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, r => r.Text.Contains("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public void SftpConnectionManagerDialog_DoubleClickConnectsSelectedConnection()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var connection = TestConnection();
        driver.BeforeReadInput = currentDriver =>
        {
            var row = currentDriver.WriteRecords.Last(r => r.Text.Contains(connection.DisplayName, StringComparison.Ordinal));
            currentDriver.EnqueueInput(new MouseConsoleInputEvent(row.X + 1, row.Y, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None));
        };

        var result = new SftpConnectionManagerDialog(screen).Show([connection]);

        Assert.NotNull(result);
        Assert.Equal(SftpConnectionManagerAction.Connect, result.Action);
        Assert.Same(connection, result.Connection);
    }

    [Fact]
    public void SftpConnectionManagerDialog_ShiftTabMovesFocusBackwardToButtons()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        var connection = TestConnection();
        driver.EnqueueKey(ShiftTab());
        driver.EnqueueKey(Key(ConsoleKey.Enter));

        var result = new SftpConnectionManagerDialog(screen).Show([connection]);

        Assert.NotNull(result);
        Assert.Equal(SftpConnectionManagerAction.Connect, result.Action);
        Assert.Same(connection, result.Connection);
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
            ShowInDriveSelection = true,
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

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private static ConsoleKeyInfo ShiftTab() =>
        new('\0', ConsoleKey.Tab, shift: true, alt: false, control: false);

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

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
