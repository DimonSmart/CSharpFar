using CSharpFar.App;
using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class DeleteCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("CSharpFarDeleteCommand_").FullName;

    [Theory]
    [InlineData(false, true, false, "Do you wish to delete permanently?")]
    [InlineData(true, true, true, "Do you wish to move to the Recycle Bin?")]
    [InlineData(true, false, false, "Do you wish to delete permanently?")]
    public void Run_UsesRecycleBinOnlyWhenEnabledAndSupported(
        bool supportsRecycleBin,
        bool settingEnabled,
        bool expectedUseRecycleBin,
        string expectedConfirmation)
    {
        string filePath = Path.Combine(_root, "file.txt");
        var fileSystem = new FakeFileSystemService();
        fileSystem.AddDirectory(_root, new FilePanelItem
        {
            Name = "file.txt",
            FullPath = filePath,
            IsDirectory = false,
            Size = 1,
            LastWriteTime = new DateTime(2026, 1, 1),
            Attributes = FileAttributes.Archive,
        });
        var fileOperations = new RecordingFileOperationService(supportsRecycleBin);
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.EnqueueKey(Key(ConsoleKey.F8));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        driver.EnqueueKey(Key(ConsoleKey.F10));
        var settings = new AppSettings();
        settings.Panels.LeftStartDirectory = _root;
        settings.Panels.RightStartDirectory = _root;
        settings.FileOperations.UseRecycleBinForDelete = settingEnabled;
        var app = new Application(
            new ScreenRenderer(driver),
            fileSystem,
            new NoOpShellService(),
            fileOperations,
            new InMemoryHistoryStore(),
            settings);
        app.Session.Panels.Left.CursorIndex = 1;

        app.Run();

        FileOperationRequest request = Assert.Single(fileOperations.Requests);
        Assert.Equal(expectedUseRecycleBin, request.Options.UseRecycleBinForDelete);
        Assert.Contains(driver.WriteRecords, record =>
            record.Text.Contains(expectedConfirmation, StringComparison.Ordinal));
        string unexpectedConfirmation = expectedUseRecycleBin
            ? "Do you wish to delete permanently?"
            : "Do you wish to move to the Recycle Bin?";
        Assert.DoesNotContain(driver.WriteRecords, record =>
            record.Text.Contains(unexpectedConfirmation, StringComparison.Ordinal));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);

    private sealed class RecordingFileOperationService(bool supportsRecycleBin) : IFileOperationService
    {
        public bool SupportsRecycleBin { get; } = supportsRecycleBin;

        public List<FileOperationRequest> Requests { get; } = [];

        public Task<FileOperationResult> ExecuteAsync(
            FileOperationRequest request,
            IProgress<FileOperationProgress>? progress,
            IFileOperationConflictResolver conflictResolver,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new FileOperationResult { Kind = request.Kind, Errors = [] });
        }
    }
}
