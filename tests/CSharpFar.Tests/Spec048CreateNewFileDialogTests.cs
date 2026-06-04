using System.Text;
using CSharpFar.App;
using CSharpFar.App.Dialogs;
using CSharpFar.App.Editor;
using CSharpFar.App.FunctionKeys;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.History;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class Spec048CreateNewFileDialogTests : IDisposable
{
    private readonly string _root;

    public Spec048CreateNewFileDialogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CSharpFarSpec048_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void OpenCreateFileDialog_ReturnsPathAndSelectedCodePage()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        EnqueueText(driver, "new.txt");
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Spacebar));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.Enter));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new OpenCreateFileDialog(screen).Show();

        Assert.NotNull(result);
        Assert.Equal("new.txt", result.FilePath);
        Assert.Equal("UTF-8 with BOM", result.CodePage.Label);
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Editor", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Open/create file:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Code page:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("{ OK }", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("[ Cancel ]", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenCreateFileDialog_MouseOpensScrollableCodePageDropdown()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        EnqueueText(driver, "mouse.txt");
        driver.EnqueueInput(new MouseConsoleInputEvent(18, 15, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueInput(new MouseConsoleInputEvent(18, 19, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new OpenCreateFileDialog(screen).Show();

        Assert.NotNull(result);
        Assert.Equal("mouse.txt", result.FilePath);
        Assert.Equal("UTF-8 with BOM", result.CodePage.Label);
        Assert.Contains(driver.WriteRecords, record =>
            record.Text.Contains("UTF-16 LE with BOM", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains('▲'));
        Assert.Contains(driver.WriteRecords, record => record.Text.Contains('▼'));
    }

    [Fact]
    public void OpenCreateFileDialog_ClosingCodePageDropdownRestoresPanelUnderlay()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        driver.WriteAt(18, 22, "UNDERLAY", ConsoleColor.White, ConsoleColor.DarkRed);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(18, 15, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        string? rowAfterDropdownClose = null;
        int readCount = 0;
        driver.BeforeReadInput = ObserveBeforeRead;

        _ = new OpenCreateFileDialog(screen).Show();

        Assert.NotNull(rowAfterDropdownClose);
        Assert.Contains("UNDERLAY", rowAfterDropdownClose, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows-1251", rowAfterDropdownClose, StringComparison.Ordinal);

        void ObserveBeforeRead(FakeConsoleDriver currentDriver)
        {
            readCount++;
            if (readCount == 3)
                rowAfterDropdownClose = currentDriver.GetRow(22);

            currentDriver.BeforeReadInput = ObserveBeforeRead;
        }
    }

    [Fact]
    public void OpenCreateFileDialog_MouseClickInPathFieldMovesTextCursor()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueInput(new MouseConsoleInputEvent(19, 12, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: true, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new OpenCreateFileDialog(screen).Show("abcdef");

        Assert.NotNull(result);
        Assert.Equal("abXcdef", result.FilePath);
    }

    [Fact]
    public void OpenCreateFileDialog_RejectsEmptyPathAndStaysOpen()
    {
        var driver = new FakeConsoleDriver(width: 100, height: 30);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.F10));
        EnqueueText(driver, "after-error.txt");
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var result = new OpenCreateFileDialog(screen).Show();

        Assert.NotNull(result);
        Assert.Equal("after-error.txt", result.FilePath);
    }

    [Fact]
    public void OpenCreateFileDialog_CompactConsoleKeepsCodePageAboveButtons()
    {
        var driver = new FakeConsoleDriver(width: 80, height: 14);
        var screen = new ScreenRenderer(driver);
        driver.EnqueueKey(Key(ConsoleKey.Escape));

        _ = new OpenCreateFileDialog(screen).Show();

        int codePageValueRow = driver.WriteRecords
            .Where(record => record.Text.Contains("Default", StringComparison.Ordinal))
            .Select(record => record.Y)
            .DefaultIfEmpty(-1)
            .Max();
        int buttonRow = driver.WriteRecords
            .Where(record => record.Text.Contains("{ OK }", StringComparison.Ordinal))
            .Select(record => record.Y)
            .DefaultIfEmpty(-1)
            .Max();

        Assert.True(codePageValueRow >= 0);
        Assert.True(buttonRow >= 0);
        Assert.True(codePageValueRow < buttonRow);
        Assert.Contains(driver.WriteRecords, record =>
            record.Text.Contains("Code page:", StringComparison.Ordinal));
    }

    [Fact]
    public void EditorFileService_SaveNewFile_UsesExplicitUtf8Bom()
    {
        string path = Path.Combine(_root, "utf8-bom.txt");
        var settings = new AppSettings.EditorSettings();
        var option = EditorNewFileEncodingOption.CreateCatalog().Single(item => item.Label == "UTF-8 with BOM");
        var service = new EditorFileService(settings);

        var session = service.Load(path, option.CreateDocumentFormat(settings));
        session.InsertText("text");
        service.Save(session);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("text", File.ReadAllText(path, Encoding.UTF8));
    }

    [Fact]
    public void EditorFileService_SaveNewFile_UsesExplicitUtf16Bom()
    {
        string path = Path.Combine(_root, "utf16-le.txt");
        var settings = new AppSettings.EditorSettings();
        var option = EditorNewFileEncodingOption.CreateCatalog().Single(item => item.Label == "UTF-16 LE with BOM");
        var service = new EditorFileService(settings);

        var session = service.Load(path, option.CreateDocumentFormat(settings));
        session.InsertText("Ж");
        service.Save(session);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xFE, bytes[1]);
        Assert.Equal("Ж", File.ReadAllText(path, Encoding.Unicode));
    }

    [Fact]
    public void EditorFileService_ExistingFile_IgnoresNewFileEncodingOption()
    {
        string path = Path.Combine(_root, "existing.txt");
        File.WriteAllText(path, "text", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var settings = new AppSettings.EditorSettings();
        var option = EditorNewFileEncodingOption.CreateCatalog().Single(item => item.Label == "UTF-16 LE with BOM");
        var service = new EditorFileService(settings);

        var session = service.Load(path, option.CreateDocumentFormat(settings));

        Assert.Equal(Encoding.UTF8.CodePage, session.Document.Format.Encoding.CodePage);
        Assert.False(session.Document.Format.EmitByteOrderMark);
    }

    [Fact]
    public void Run_ShiftF4OpensOpenCreateFileDialog()
    {
        var fs = new FakeFileSystemService();
        fs.AddDirectory(_root);
        var driver = new FakeConsoleDriver(width: 100, height: 14);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F4, shift: true, alt: false, control: false));
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        var app = CreateApp(fs, driver);
        app.Run();

        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Open/create file:", StringComparison.Ordinal));
        Assert.Contains(driver.WriteRecords, r => r.Text.Contains("Code page:", StringComparison.Ordinal));
    }

    [Fact]
    public void ShiftFunctionKeyLayerShowsNewFileCommand()
    {
        var provider = new DefaultFunctionKeyBindingProvider();

        var binding = provider.GetBindings().Single(item =>
            item.Layer == FunctionKeyLayer.Shift &&
            item.Key == ConsoleKey.F4);

        Assert.Equal(FunctionKeyCommandIds.OpenCreateFile, binding.CommandId);
        Assert.Equal("New", binding.Label);
    }

    private Application CreateApp(FakeFileSystemService fs, FakeConsoleDriver driver)
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
            settings);
    }

    private static void EnqueueText(FakeConsoleDriver driver, string text)
    {
        foreach (char ch in text)
            driver.EnqueueKey(new ConsoleKeyInfo(ch, ConsoleKey.None, shift: false, alt: false, control: false));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new('\0', key, shift: false, alt: false, control: false);
}
