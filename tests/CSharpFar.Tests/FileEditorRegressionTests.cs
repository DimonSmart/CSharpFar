using System.Reflection;
using System.Text;
using CSharpFar.App.Editor;
using CSharpFar.Console;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Core.Services;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed class FileEditorRegressionTests : IDisposable
{
    private readonly string _tempDir;

    public FileEditorRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarEditorRegression_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(false, "\t")]
    [InlineData(true, "    ")]
    public void Show_TabInsertsTextAccordingToExpandTabs(bool expandTabs, string expected)
    {
        string filePath = Path.Combine(_tempDir, $"tab-{expandTabs}.txt");
        File.WriteAllText(filePath, string.Empty);
        var settings = new AppSettings.EditorSettings
        {
            ExpandTabs = expandTabs,
            TabSize = 4,
        };

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, settings);

        Assert.Equal(expected, File.ReadAllText(filePath));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public void MoveToFindStart_RespectsF7StartsAtNextCharacter(bool startsAtNextCharacter, int expectedColumn)
    {
        var settings = new AppSettings.EditorSettings
        {
            F7StartsAtNextCharacter = startsAtNextCharacter,
        };
        var editor = CreateFileEditor(new ScreenRenderer(new FakeConsoleDriver(80, 25)), settings);
        var format = new EditorDocumentFormat(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            emitByteOrderMark: false,
            EditorLineEnding.Lf,
            "UTF-8");
        var session = new EditorSession(
            "find.txt",
            new EditorDocument(EditorTextBuffer.FromText("hello hello"), format),
            settings,
            readOnly: false);

        MethodInfo method = typeof(FileEditor).GetMethod(
            "MoveToFindStart",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(editor, [session, false]);

        Assert.Equal(expectedColumn, session.Cursor.Column);
    }

    [Fact]
    public void Show_TwoRowTerminalDoesNotRenderContentOverFunctionKeyBar()
    {
        string filePath = Path.Combine(_tempDir, "tiny-two-rows.txt");
        File.WriteAllText(filePath, "abc");
        var driver = new FakeConsoleDriver(width: 40, height: 2);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.DoesNotContain(driver.WriteRecords, record =>
            record.Y == 1 && record.Text.Contains("abc", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_ThreeRowTerminalOmitsStatusInsteadOfOverlappingFunctionKeyBar()
    {
        string filePath = Path.Combine(_tempDir, "tiny-three-rows.txt");
        File.WriteAllText(filePath, "abc");
        var driver = new FakeConsoleDriver(width: 40, height: 3);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.DoesNotContain(driver.WriteRecords, record =>
            record.Text.StartsWith(" Ln ", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 1 && record.Text.Contains("abc", StringComparison.Ordinal));
    }

    [Fact]
    public void Fit_UsesDisplayCellsAndDoesNotSplitSurrogatePairs()
    {
        MethodInfo method = typeof(FileEditor).GetMethod(
            "Fit",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        string fitted = (string)method.Invoke(null, ["A😀B", 2])!;

        Assert.Equal("A ", fitted);
    }

    [Fact]
    public void Show_RemoteLoadFailureLeavesNoActiveSourceState()
    {
        var source = new ThrowingPanelSource();
        var registry = new FilePanelSourceRegistry([source]);
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        var editor = CreateFileEditor(new ScreenRenderer(driver), new AppSettings.EditorSettings(), registry);

        editor.Show(new PanelLocation(source.SourceId, "/broken.txt"));

        FieldInfo sourceField = typeof(FileEditor).GetField("_activeSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo pathField = typeof(FileEditor).GetField("_activeSourcePath", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Null(sourceField.GetValue(editor));
        Assert.Null(pathField.GetValue(editor));
    }

    private static void ShowFileEditor(
        ScreenRenderer renderer,
        string filePath,
        AppSettings.EditorSettings? settings = null)
    {
        CreateFileEditor(renderer, settings ?? new AppSettings.EditorSettings()).Show(filePath);
    }

    private static FileEditor CreateFileEditor(
        ScreenRenderer renderer,
        AppSettings.EditorSettings settings,
        FilePanelSourceRegistry? sourceRegistry = null)
    {
        UiTestHost host = UiTestHost.Create(renderer);
        var fields = new FormFieldFactory(
            new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore()));
        return new FileEditor(
            host.Surfaces,
            host.ModalDialogs,
            new DialogService(host.ModalDialogs, fields),
            null,
            settings,
            null,
            fields,
            null,
            null,
            sourceRegistry);
    }

    private sealed class ThrowingPanelSource : IFilePanelSource
    {
        public PanelSourceId SourceId { get; } = new("test-throwing-editor-source");
        public string DisplayName => "Throwing";
        public PanelProviderCapabilities Capabilities => default;
        public IReadOnlyCollection<char> PathSeparators { get; } = ['/'];

        public string NormalizePath(string sourcePath) => sourcePath;
        public bool IsRootPath(string sourcePath) => false;
        public string? GetParentPath(string sourcePath) => "/";
        public IReadOnlyList<FilePanelItem> EnumerateDirectory(string sourcePath, CancellationToken cancellationToken = default) => [];

        public FilePanelItem? GetItem(string sourcePath, CancellationToken cancellationToken = default) =>
            new()
            {
                Name = Path.GetFileName(sourcePath),
                FullPath = sourcePath,
                SourceId = SourceId,
                IsDirectory = false,
                Size = 1,
            };

        public Task<Stream> OpenReadAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new IOException("Synthetic load failure.");

        public Task<Stream> OpenWriteAsync(string sourcePath, bool overwrite, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateDirectoryAsync(string sourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string sourcePath, bool recursive, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RenameAsync(string sourcePath, string newSourcePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
