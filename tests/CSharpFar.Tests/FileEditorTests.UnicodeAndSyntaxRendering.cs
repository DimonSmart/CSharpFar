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

public sealed partial class FileEditorTests
{
    [Fact]
    public void Show_SelectionOnEmptyLine_IsRenderedWithInvertedStyle()
    {
        string filePath = Path.Combine(_tempDir, "empty-selection.txt");
        File.WriteAllText(filePath, "a\n\nb");
        var palette = CSharpFarPaletteRegistry.Default;

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

    [Fact]
    public void Show_RightArrowDoesNotSplitUtf8FourByteCharacter()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte.txt");
        File.WriteAllText(filePath, "A" + smile + "B");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("A" + smile + "XB", File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_ScreenCursorUsesEmojiDisplayWidth()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-cursor-column.txt");
        File.WriteAllText(filePath, "A" + smile + "B");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal(3, driver.CursorX);
    }

    [Fact]
    public void Show_WideEmojiCursorUsesFullGlyphStyleAndHidesNativeCursor()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-custom-cursor.txt");
        File.WriteAllText(filePath, "A" + smile + "B");
        var palette = CSharpFarPaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 1 &&
            record.Y == 1 &&
            record.Text == smile &&
            record.Background == palette.CommandLineFg);
        Assert.False(driver.CursorVisible);
    }

    [Fact]
    public void Show_WideEmojiCursorBlinksByTogglingFullGlyphStyle()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-blink-cursor.txt");
        File.WriteAllText(filePath, "A" + smile + "B");
        var palette = CSharpFarPaletteRegistry.Default;

        bool sawCursorOn = false;
        bool sawCursorOffAfterOn = false;
        int polls = 0;
        var highlighter = new StaticSyntaxHighlighter();
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeReadInput = currentDriver =>
        {
            currentDriver.BeforeTryReadInput = ObserveBlink;
        };
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));

        ShowFileEditor(
            new ScreenRenderer(driver),
            filePath,
            syntaxHighlighter: highlighter);

        Assert.True(sawCursorOn);
        Assert.True(sawCursorOffAfterOn);
        Assert.Equal(2, highlighter.CallCount);

        void ObserveBlink(FakeConsoleDriver currentDriver)
        {
            polls++;
            bool cursorOn =
                currentDriver.GetCell(1, 1).Background == palette.CommandLineFg &&
                currentDriver.GetCell(2, 1).Background == palette.CommandLineFg;
            bool cursorOff =
                currentDriver.GetCell(1, 1).Background == palette.PanelBackground &&
                currentDriver.GetCell(2, 1).Background == palette.PanelBackground;

            sawCursorOn |= cursorOn;
            sawCursorOffAfterOn |= sawCursorOn && cursorOff;
            if (sawCursorOffAfterOn || polls > 60)
            {
                currentDriver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));
                return;
            }

            currentDriver.BeforeTryReadInput = ObserveBlink;
        }
    }

    [Fact]
    public void Show_StatusReportsUtf8FourByteCharacterCode()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-status.txt");
        File.WriteAllText(filePath, smile);

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.Y == 23 &&
            record.Text.Contains("Col 1", StringComparison.Ordinal) &&
            record.Text.Contains("U+1F642/128578", StringComparison.Ordinal));
    }

    [Fact]
    public void Show_EnterAtEndOfUtf8FourByteLineMovesCursorToNextLineStart()
    {
        string smile = char.ConvertFromUtf32(0x1F642);
        string grin = char.ConvertFromUtf32(0x1F600);
        string gothicLetter = char.ConvertFromUtf32(0x10348);
        string line = "ascii A " + smile + " " + grin + " " + gothicLetter + " Z";
        string filePath = Path.Combine(_tempDir, "utf8-four-byte-enter-at-end.txt");
        File.WriteAllText(filePath, line);

        int cursorXAfterEnter = -1;
        int cursorYAfterEnter = -1;
        var driver = new FakeConsoleDriver(80, 25);
        driver.BeforeTryReadInput = currentDriver =>
            currentDriver.BeforeTryReadInput = afterEnd =>
                afterEnd.BeforeTryReadInput = afterEnter =>
                {
                    cursorXAfterEnter = afterEnter.CursorX;
                    cursorYAfterEnter = afterEnter.CursorY;
                };
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.End, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('X', ConsoleKey.X, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F2, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal(0, cursorXAfterEnter);
        Assert.Equal(2, cursorYAfterEnter);
        Assert.Equal(line + "\nX", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_SyntaxSpanAppliesTokenStyle()
    {
        string filePath = Path.Combine(_tempDir, "syntax-render.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter(
            new EditorColorSpan(0, 1, 1, new CellStyle(ConsoleColor.Yellow, ConsoleColor.Black)));

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, syntaxHighlighter: highlighter);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 1 &&
            record.Y == 1 &&
            record.Text == "b" &&
            record.Foreground == ConsoleColor.Yellow);
    }

    [Fact]
    public void Show_PlainEditorTextUsesPanelBackground()
    {
        string filePath = Path.Combine(_tempDir, "plain-editor-background.unknown");
        File.WriteAllText(filePath, "abc");
        var palette = CSharpFarPaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 0 &&
            record.Y == 1 &&
            record.Text.StartsWith('a') &&
            record.Background == palette.PanelBackground);
    }

    [Fact]
    public void Show_SelectionStyleOverridesSyntaxSpan()
    {
        string filePath = Path.Combine(_tempDir, "syntax-selection.txt");
        File.WriteAllText(filePath, "abc");
        var highlighter = new StaticSyntaxHighlighter(
            new EditorColorSpan(0, 0, 3, new CellStyle(ConsoleColor.Red, ConsoleColor.Black)));
        var palette = CSharpFarPaletteRegistry.Default;

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F3, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: false));
        driver.EnqueueKey(new ConsoleKeyInfo('\0', ConsoleKey.F10, shift: false, alt: false, control: false));

        ShowFileEditor(new ScreenRenderer(driver), filePath, syntaxHighlighter: highlighter);

        Assert.Contains(driver.WriteRecords, record =>
            record.X == 0 &&
            record.Y == 1 &&
            record.Text == "a" &&
            record.Background == palette.CommandLineFg);
    }

}
