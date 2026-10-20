using CSharpFar.App.Editor;
using CSharpFar.App.Viewer;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Abstractions;
using CSharpFar.Core.Models;
using CSharpFar.Tests.Fakes;
using CSharpFar.Ui;

namespace CSharpFar.Tests;

public sealed partial class FileEditorTests : IDisposable
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
    public void Show_CursorMovementInsideViewportOnlyUpdatesCursorAndStatus()
    {
        string filePath = Path.Combine(_tempDir, "cursor-partial.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter();
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeTryReadInput = current => current.ClearRecordedOperations();
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            syntaxHighlighter: highlighter);

        Assert.Equal(1, highlighter.CallCount);
        Assert.Equal(1, driver.CursorX);
        Assert.DoesNotContain(driver.WriteRecords, write => write.Y is 0 or 1 or 24);
        Assert.Contains(driver.WriteRecords, write => write.Y == 23);
    }

    [Fact]
    public void Show_CursorMovementThatScrollsViewportUsesFullRender()
    {
        string filePath = Path.Combine(_tempDir, "cursor-scroll-full.txt");
        File.WriteAllText(filePath, "abcdef");
        var highlighter = new StaticSyntaxHighlighter();
        var driver = new FakeConsoleDriver(width: 5, height: 6);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            syntaxHighlighter: highlighter);

        Assert.Equal(2, highlighter.CallCount);
        Assert.Contains(driver.WriteRecords, write => write.Y == 1);
    }

    [Fact]
    public void Show_SelectionChangeUsesFullRender()
    {
        string filePath = Path.Combine(_tempDir, "selection-full.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter();
        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: true, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            syntaxHighlighter: highlighter);

        Assert.Equal(2, highlighter.CallCount);
    }

    [Fact]
    public void Show_InterruptedCursorPartialRetriesAsFullForNewGeometry()
    {
        string filePath = Path.Combine(_tempDir, "cursor-partial-resize.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter();
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeTryReadInput = current =>
        {
            current.ResizeAfterWriteCount = 1;
            current.ResizeAfterWrite = resized => resized.SetSize(100, 30);
        };
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            syntaxHighlighter: highlighter);

        Assert.Equal(new ConsoleSize(100, 30), driver.GetSize());
        Assert.Equal(2, highlighter.CallCount);
        Assert.Equal(1, driver.CursorX);
        Assert.Equal(1, driver.CursorY);
    }

    [Fact]
    public void Show_ModifierChangeOnlyUpdatesFunctionKeyBar()
    {
        string filePath = Path.Combine(_tempDir, "modifier-partial.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter();
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeTryReadInput = current => current.ClearRecordedOperations();
        driver.EnqueueInput(new ModifierKeyConsoleInputEvent(ConsoleModifiers.Alt));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            syntaxHighlighter: highlighter);

        Assert.Equal(1, highlighter.CallCount);
        Assert.NotEmpty(driver.WriteRecords);
        Assert.All(driver.WriteRecords, write => Assert.Equal(24, write.Y));
    }

    [Fact]
    public void Show_FunctionKeyBarMouseClickSavesAndClosesEditor()
    {
        string filePath = Path.Combine(_tempDir, "mouse-keybar.txt");
        File.WriteAllText(filePath, "abc");

        var driver = new FakeConsoleDriver(width: 120, height: 25);
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueInput(LeftMouse(x: 10, y: 24));
        driver.EnqueueInput(LeftMouse(x: 90, y: 24));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("Xabc", File.ReadAllText(filePath));
        Assert.Contains("10Close", ComposeRow(driver, y: 24, width: 120));
    }

    [Fact]
    public void Show_MouseWheelScrollsTextDown()
    {
        string filePath = Path.Combine(_tempDir, "editor-wheel.txt");
        File.WriteAllText(filePath, string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")));

        var driver = new FakeConsoleDriver(width: 80, height: 8);
        driver.EnqueueInput(MouseWheelDown());
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains("line4", string.Concat(driver.WriteRecords.Select(record => record.Text)));
    }

    [Fact]
    public void Show_MouseWheelRedrawDoesNotRewriteFunctionKeyBar()
    {
        string filePath = Path.Combine(_tempDir, "editor-wheel-keybar.txt");
        File.WriteAllText(filePath, string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line{i}")));

        var driver = new FakeConsoleDriver(width: 80, height: 8);
        var wheelRedrawWrites = Array.Empty<FakeConsoleDriver.WriteRecord>();
        driver.BeforeReadInput = firstRead =>
        {
            firstRead.ClearRecordedOperations();
            firstRead.BeforeReadInput = secondRead =>
            {
                wheelRedrawWrites = secondRead.WriteRecords.ToArray();
            };
        };
        driver.EnqueueInput(MouseWheelDown());
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.DoesNotContain(wheelRedrawWrites, record => record.Y == 7);
    }

    private static void ShowFileEditor(
        ScreenRenderer renderer,
        string filePath,
        AppSettings.EditorSettings? settings = null,
        ITextClipboard? clipboard = null,
        EditorFileNameInsertionContext? fileNameInsertionContext = null,
        IEditorSyntaxHighlighter? syntaxHighlighter = null)
    {
        UiTestHost host = UiTestHost.Create(renderer);
        var editor = new FileEditor(
            host.Surfaces,
            host.ModalDialogs,
            new DialogService(host.ModalDialogs, new FormFieldFactory(new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore()))),
            null,
            settings,
            clipboard,
            new FormFieldFactory(new SingleLineTextHistoryRegistry(new InMemorySingleLineTextHistoryStore())),
            fileNameInsertionContext,
            syntaxHighlighter);
        editor.Show(filePath);
    }

    private static MouseConsoleInputEvent LeftMouse(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Down, MouseKeyModifiers.None);

    private static MouseConsoleInputEvent LeftMouseDoubleClick(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.DoubleClick, MouseKeyModifiers.None);

    private static MouseConsoleInputEvent MouseMove(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Move, MouseKeyModifiers.None);

    private static MouseConsoleInputEvent MouseUp(int x, int y) =>
        new(x, y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None);

    private static MouseConsoleInputEvent MouseWheelDown() =>
        new(0, 1, MouseButton.WheelDown, MouseEventKind.Wheel, MouseKeyModifiers.None);

    private static void ClearRecordedOperationsBeforeFirstInput(FakeConsoleDriver driver)
    {
        bool cleared = false;
        driver.BeforeReadInput = currentDriver =>
        {
            if (cleared)
                return;

            currentDriver.ClearRecordedOperations();
            cleared = true;
        };
    }

    private static string ComposeRow(FakeConsoleDriver driver, int y, int width)
    {
        var row = Enumerable.Repeat(' ', width).ToArray();
        foreach (var record in driver.WriteRecords.Where(record => record.Y == y))
        {
            for (int i = 0; i < record.Text.Length && record.X + i < width; i++)
                row[record.X + i] = record.Text[i];
        }

        return new string(row);
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

    private sealed class StaticSyntaxHighlighter : IEditorSyntaxHighlighter
    {
        private readonly IReadOnlyList<EditorColorSpan> _spans;

        public StaticSyntaxHighlighter(params EditorColorSpan[] spans)
        {
            _spans = spans;
        }

        public int CallCount { get; private set; }

        public EditorSyntaxHighlightResult Highlight(EditorSyntaxHighlightRequest request)
        {
            CallCount++;
            return new(_spans, EditorSyntaxDiagnostics.Active("Fake", "Fake", "palette"));
        }
    }

    [Fact]
    public void Show_AssociatedTextInputPreservesCompleteUnicodeText()
    {
        string filePath = Path.Combine(_tempDir, "associated-text.txt");
        File.WriteAllText(filePath, "");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueInput(new KeyConsoleInputEvent(
            new ConsoleKeyInfo('\0', ConsoleKey.NoName, shift: false, alt: false, control: false))
        {
            Text = "Ж😀á",
        });
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("Ж😀á", File.ReadAllText(filePath));
    }

}
