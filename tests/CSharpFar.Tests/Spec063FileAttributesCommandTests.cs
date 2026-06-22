using CSharpFar.App;
using CSharpFar.App.Dialogs;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec063FileAttributesCommandTests : IDisposable
{
    private readonly string _root;

    public Spec063FileAttributesCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec063_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FunctionKeyBindings_ContainsShiftF9Attributes()
    {
        var binding = new DefaultFunctionKeyBindingProvider()
            .GetBindings()
            .Single(binding => binding.CommandId == FunctionKeyCommandIds.Attributes);

        Assert.Equal(FunctionKeyLayer.Shift, binding.Layer);
        Assert.Equal(ConsoleKey.F9, binding.Key);
        Assert.Equal("Attr", binding.Label);
    }

    [Fact]
    public void Run_ShiftF9AppliesDialogChangeSetAndRefreshesPanel()
    {
        string path = Path.Combine(_root, "file.txt");
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root, Item("file.txt", path));
        var metadata = new RecordingMetadataService(Snapshot(path));
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F9, shift: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, metadata, new StubAttributesDialog(new FileAttributesDialogResult(
            new FileMetadataChangeSet(
                new Dictionary<FileAttributeId, AttributeEditState>
                {
                    [FileAttributeId.ReadOnly] = AttributeEditState.Checked,
                },
                null,
                null,
                null,
                new Dictionary<UnixPermissionBit, AttributeEditState>()),
            OpenSystemProperties: false)), driver);
        app.Session.Panels.Left.CursorIndex = 1;

        app.Run();

        Assert.Equal([path], metadata.AppliedPaths);
        Assert.Single(metadata.AppliedChanges);
        Assert.True(fs.ReadDirectoryCallCount > 1);
    }

    [Fact]
    public void Run_ParentDirectoryDoesNotOpenDialog()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root, new FilePanelItem
        {
            Name = "..",
            FullPath = Directory.GetParent(_root)!.FullName,
            IsDirectory = true,
            IsParentDirectory = true,
        });
        var metadata = new RecordingMetadataService(Snapshot(_root));
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F9, shift: true));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var dialog = new StubAttributesDialog(null);

        CreateApp(fs, metadata, dialog, driver).Run();

        Assert.Equal(0, dialog.ShowCount);
        Assert.Empty(metadata.AppliedChanges);
    }

    private Application CreateApp(
        FakeFileSystemService fs,
        IFileMetadataService metadata,
        IFileAttributesDialog dialog,
        FakeConsoleDriver driver)
    {
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;

        return new Application(
            new ScreenRenderer(driver),
            fs,
            new NoOpShellService(),
            new NoOpFileOperationService(),
            new InMemoryHistoryStore(),
            settings,
            fileMetadata: metadata,
            fileAttributesDialogFactory: () => dialog);
    }

    private static FilePanelItem Item(string name, string path) =>
        new()
        {
            Name = name,
            FullPath = path,
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        };

    private static FileMetadataSnapshot Snapshot(string path) =>
        new(
            path,
            Path.GetFileName(path),
            false,
            FileAttributes.Archive,
            DateTime.Now,
            DateTime.Now,
            DateTime.Now,
            null,
            [new FileAttributeDescriptor(FileAttributeId.ReadOnly, "Read only", 'R', true, true)],
            new Dictionary<FileAttributeId, AttributeEditState>
            {
                [FileAttributeId.ReadOnly] = AttributeEditState.Unchecked,
            },
            true,
            true,
            true,
            null);

    private static ConsoleKeyInfo Key(ConsoleKey key, bool shift = false) =>
        new('\0', key, shift, alt: false, control: false);

    private sealed class StubAttributesDialog : IFileAttributesDialog
    {
        private readonly FileAttributesDialogResult? _result;

        public StubAttributesDialog(FileAttributesDialogResult? result)
        {
            _result = result;
        }

        public int ShowCount { get; private set; }

        public FileAttributesDialogResult? Show(FileMetadataSnapshot snapshot)
        {
            ShowCount++;
            return _result;
        }
    }

    private sealed class RecordingMetadataService : IFileMetadataService
    {
        private readonly FileMetadataSnapshot _snapshot;

        public RecordingMetadataService(FileMetadataSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public IReadOnlyList<string> AppliedPaths { get; private set; } = [];
        public List<FileMetadataChangeSet> AppliedChanges { get; } = [];

        public FileMetadataSnapshot GetMetadata(string path) => _snapshot;

        public FileMetadataSnapshot GetMergedMetadata(IReadOnlyList<string> paths) => _snapshot;

        public FileMetadataApplyResult ApplyMetadata(IReadOnlyList<string> paths, FileMetadataChangeSet changes)
        {
            AppliedPaths = paths.ToList();
            AppliedChanges.Add(changes);
            return new FileMetadataApplyResult(paths.Count, paths.Count, []);
        }
    }
}
