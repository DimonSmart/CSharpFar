using System.Reflection;
using CSharpFar.App.Editor;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Core.Abstractions;
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
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("a", clipboard.Text);
        Assert.Equal("abca", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlC_CopiesSelectionToSystemClipboard()
    {
        string filePath = Path.Combine(_tempDir, "system-copy.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("ab", clipboard.Text);
    }

    [Fact]
    public void Show_CtrlV_PastesFromSystemClipboard()
    {
        string filePath = Path.Combine(_tempDir, "system-paste.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard { Text = "Z" };

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("abcZ", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlV_WithEmptySystemClipboard_DoesNotPastePreviousCopy()
    {
        string filePath = Path.Combine(_tempDir, "empty-system-clipboard.txt");
        File.WriteAllText(filePath, "abc");
        var clipboard = new FakeTextClipboard { ClearAfterSet = true };

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0016', ConsoleKey.V, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("abc", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_CtrlAAndCtrlC_CopiesUtf8BomFileTextWithoutBom()
    {
        string filePath = Path.Combine(_tempDir, "system-copy-bom.json");
        File.WriteAllText(filePath, "{\"items\":[1,2]}", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        Assert.Equal("{\"items\":[1,2]}", clipboard.Text);
        Assert.False(clipboard.Text.StartsWith('\uFEFF'));
    }

    [Fact]
    public void Show_CtrlAAndCtrlC_NormalizesSystemClipboardLineEndingsOnWindows()
    {
        string filePath = Path.Combine(_tempDir, "system-copy-lines.txt");
        File.WriteAllText(filePath, "a\nb");
        var clipboard = new FakeTextClipboard();

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, clipboard: clipboard);

        string expected = OperatingSystem.IsWindows() ? "a\r\nb" : "a\nb";
        Assert.Equal(expected, clipboard.Text);
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
        AppSettings.EditorSettings? settings = null,
        ITextClipboard? clipboard = null)
    {
        var editorType = typeof(TextFileReader).Assembly.GetType("CSharpFar.App.Editor.FileEditor", throwOnError: true)!;
        var editor = Activator.CreateInstance(
            editorType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: clipboard is null && settings is null
                ? [renderer]
                : [renderer, null, settings, clipboard],
            culture: null)!;

        editorType.GetMethod("Show", BindingFlags.Instance | BindingFlags.Public)!
            .Invoke(editor, [filePath]);
    }

    private sealed class FakeTextClipboard : ITextClipboard
    {
        public string Text { get; set; } = string.Empty;
        public bool ClearAfterSet { get; set; }

        public bool TrySetText(string text)
        {
            Text = text;
            if (ClearAfterSet)
                Text = string.Empty;
            return true;
        }

        public bool TryGetText(out string text)
        {
            text = Text;
            return true;
        }
    }
}
