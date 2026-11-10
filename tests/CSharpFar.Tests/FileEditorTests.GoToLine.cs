using CSharpFar.App.Editor;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed partial class FileEditorTests
{
    [Theory]
    [InlineData("1", 0)]
    [InlineData("3", 2)]
    [InlineData("5", 4)]
    [InlineData("999", 4)]
    [InlineData(" 3 ", 2)]
    public void Show_AltF8MovesToRequestedLine(string input, int targetIndex)
    {
        string filePath = Path.Combine(_tempDir, "go-to-line.txt");
        string[] lines = ["one", "two", "three", "four", "five"];
        File.WriteAllText(filePath, string.Join("\n", lines));

        var driver = new FakeConsoleDriver(80, 25);
        EnqueueGoToLine(driver, input);
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        lines[targetIndex] = "X" + lines[targetIndex];
        Assert.Equal(string.Join("\n", lines), File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("Ж😀", "XЖ😀")]
    [InlineData("one", "Xone")]
    public void Show_AltF8WorksInSingleLineFiles(string original, string expected)
    {
        string filePath = Path.Combine(_tempDir, "single-line.txt");
        File.WriteAllText(filePath, original);

        var driver = new FakeConsoleDriver(80, 25);
        EnqueueGoToLine(driver, "99");
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal(expected, File.ReadAllText(filePath));
    }

    [Fact]
    public void Show_AltF8MovesToBeginningOfUnicodeLine()
    {
        string filePath = Path.Combine(_tempDir, "unicode-go-to.txt");
        File.WriteAllText(filePath, "first\n😀 строка\nlast");

        var driver = new FakeConsoleDriver(80, 25);
        EnqueueGoToLine(driver, "2");
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("first\nX😀 строка\nlast", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    public void Show_AltF8RejectsInvalidInputWithoutMovingCursor(string input)
    {
        string filePath = Path.Combine(_tempDir, "invalid-line.txt");
        File.WriteAllText(filePath, "one\ntwo\nthree");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.DownArrow));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        EnqueueGoToLine(driver, input);
        driver.EnqueueKey(Key(ConsoleKey.Escape));
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("one\ntXwo\nthree", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F10)]
    public void Show_AltF8CancellationPreservesSelectionAndMarkMode(ConsoleKey cancel)
    {
        string filePath = Path.Combine(_tempDir, "cancel-go-to.txt");
        File.WriteAllText(filePath, "abc\ndef");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.F3));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.F8, alt: true));
        driver.EnqueueKey(Key(cancel));
        driver.EnqueueKey(Key(ConsoleKey.F8));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("bc\ndef", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(ConsoleKey.Escape)]
    [InlineData(ConsoleKey.F10)]
    public void Show_AltF8CancellationPreservesDistantCursorAndViewport(ConsoleKey cancel)
    {
        string filePath = Path.Combine(_tempDir, "cancel-distant.txt");
        string[] lines = Enumerable.Range(1, 50).Select(number => $"line-{number:00}").ToArray();
        File.WriteAllText(filePath, string.Join("\n", lines));

        var driver = new FakeConsoleDriver(80, 8);
        driver.EnqueueKey(Key(ConsoleKey.End, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F8, alt: true));
        driver.EnqueueKey(Key(cancel));
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        lines[^1] += "X";
        Assert.Equal(string.Join("\n", lines), File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_AltF8ClearsSelectionAndMarkMode()
    {
        string filePath = Path.Combine(_tempDir, "clear-mark.txt");
        File.WriteAllText(filePath, "abc\ndef\nghi");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.F3));
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        EnqueueGoToLine(driver, "3");
        driver.EnqueueKey(Key(ConsoleKey.RightArrow));
        driver.EnqueueKey(Key(ConsoleKey.F8));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("abc\ndef", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_AltF8NavigatesInReadOnlyEditor()
    {
        string filePath = Path.Combine(_tempDir, "readonly-go-to.txt");
        File.WriteAllText(filePath, "one\ntwo\nthree");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        var driver = new FakeConsoleDriver(80, 25);
        EnqueueGoToLine(driver, "2");
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("one\ntwo\nthree", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
        Assert.Equal(2, driver.CursorY);
    }

    [Fact]
    public void Show_AltF8MakesDistantLineVisibleAndUpdatesStatus()
    {
        string filePath = Path.Combine(_tempDir, "far-line.txt");
        string[] lines = Enumerable.Range(1, 70).Select(number => $"line-{number:00}").ToArray();
        File.WriteAllText(filePath, string.Join("\n", lines));

        var driver = new FakeConsoleDriver(80, 8);
        EnqueueGoToLine(driver, "56");
        driver.EnqueueInput(LeftMouse(0, 5));
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        lines[55] = "Xline-56";
        Assert.Equal(string.Join("\n", lines), File.ReadAllText(filePath).ReplaceLineEndings("\n"));
        Assert.InRange(driver.CursorY, 1, 5);
    }

    [Fact]
    public void Show_AltF8PreservesRedoHistory()
    {
        string filePath = Path.Combine(_tempDir, "redo-go-to.txt");
        File.WriteAllText(filePath, "one\ntwo");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.X, 'X'));
        driver.EnqueueKey(Key(ConsoleKey.Z, control: true));
        EnqueueGoToLine(driver, "2");
        driver.EnqueueKey(Key(ConsoleKey.Z, shift: true, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("Xone\ntwo", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Show_ModifiedF8NeverDeletesText()
    {
        string filePath = Path.Combine(_tempDir, "modified-f8.txt");
        File.WriteAllText(filePath, "one\ntwo");

        var driver = new FakeConsoleDriver(80, 25);
        driver.EnqueueKey(Key(ConsoleKey.F8, shift: true));
        driver.EnqueueKey(Key(ConsoleKey.F8, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F8, shift: true, alt: true));
        driver.EnqueueKey(Key(ConsoleKey.F8, control: true, alt: true));
        driver.EnqueueKey(Key(ConsoleKey.F8, shift: true, control: true));
        driver.EnqueueKey(Key(ConsoleKey.F2));
        driver.EnqueueKey(Key(ConsoleKey.F10));

        ShowFileEditor(new ScreenRenderer(driver), filePath);

        Assert.Equal("one\ntwo", File.ReadAllText(filePath).ReplaceLineEndings("\n"));
    }

    private static ConsoleKeyInfo Key(
        ConsoleKey key, char character = '\0', bool shift = false, bool alt = false, bool control = false) =>
        new(character, key, shift, alt, control);

    private static void EnqueueGoToLine(FakeConsoleDriver driver, string input)
    {
        driver.EnqueueKey(Key(ConsoleKey.F8, alt: true));
        foreach (char character in input)
        {
            ConsoleKey key = character switch
            {
                >= '0' and <= '9' => (ConsoleKey)((int)ConsoleKey.D0 + character - '0'),
                >= 'a' and <= 'z' => (ConsoleKey)((int)ConsoleKey.A + character - 'a'),
                ' ' => ConsoleKey.Spacebar,
                '-' => ConsoleKey.OemMinus,
                _ => throw new ArgumentOutOfRangeException(nameof(input)),
            };
            driver.EnqueueKey(Key(key, character));
        }
        driver.EnqueueKey(Key(ConsoleKey.Enter, '\r'));
    }
}
