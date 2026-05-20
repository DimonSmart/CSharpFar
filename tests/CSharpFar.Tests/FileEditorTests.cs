using System.Reflection;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Models;
using CSharpFar.Ui;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class FileEditorTests : IDisposable
{
    private readonly string _tempDir;

    public FileEditorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CSharpFarEditorTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            foreach (string path in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Show_ReadOnlyFileIgnoresTypingAndExitsCleanly()
    {
        string filePath = Path.Combine(_tempDir, "readonly.txt");
        File.WriteAllText(filePath, "original");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("original", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_WhenReadOnlyLockDisabled_StaysOpenWhenSaveOnExitFails()
    {
        string filePath = Path.Combine(_tempDir, "readonly-save.txt");
        File.WriteAllText(filePath, "original");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('S', ConsoleKey.S, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('D', ConsoleKey.D, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            new AppSettings.EditorSettings { OpenReadOnlyFilesReadOnly = false });

        Assert.Throws<InvalidOperationException>(() => driver.ReadKey(intercept: true));
        Assert.Equal("original", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_ModifierInputSwitchesEditorFunctionKeyBar()
    {
        string filePath = Path.Combine(_tempDir, "keys.txt");
        File.WriteAllText(filePath, "text");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Alt));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record => record.Text.Contains("Prev", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_F3MarkAndF6Move_CutSelectedText()
    {
        string filePath = Path.Combine(_tempDir, "mark-move.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F6, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("c", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_F5CopyAndPaste_UsesSelectedText()
    {
        string filePath = Path.Combine(_tempDir, "mark-copy.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("abca", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlASelectsAllText()
    {
        string filePath = Path.Combine(_tempDir, "ctrl-a.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("X", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_SelectionOnEmptyLine_IsRenderedWithInvertedStyle()
    {
        string filePath = Path.Combine(_tempDir, "empty-selection.txt");
        File.WriteAllText(filePath, "a\n\nb");
        var palette = PaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 2 &&
            record.Background == palette.CommandLineFg);
    }

    [Fact]
    public void Show_CursorAfterLineEnd_ShowsLineEndingInStatus()
    {
        string filePath = Path.Combine(_tempDir, "line-ending-status.txt");
        File.WriteAllText(filePath, "a\r\nb");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("CRLF", StringComparison.Ordinal));
        Assert.DoesNotContain(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("Col 2  EOF", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_CursorAtDocumentEnd_StillShowsEofInStatus()
    {
        string filePath = Path.Combine(_tempDir, "eof-status.txt");
        File.WriteAllText(filePath, "a");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("EOF", StringComparison.Ordinal));
    }

    private static void ShowFileEditor(
        ScreenRenderer renderer,
        string filePath,
        AppSettings.EditorSettings? settings = null)
    {
        var editorType = typeof(TextFileReader).Assembly.GetType("CSharpFar.App.Editor.FileEditor", throwOnError: true)!;
        var editor = Activator.CreateInstance(
            editorType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: settings is null ? [renderer] : [renderer, null, settings],
            culture: null)!;

        editorType.GetMethod("Show", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(editor, [filePath]);
    }
}
