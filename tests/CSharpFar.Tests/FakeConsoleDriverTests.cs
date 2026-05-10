using CSharpFar.Console.Models;
using CSharpFar.Console.Input;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public class FakeConsoleDriverTests
{
    [Fact]
    public void WriteAt_StoresCharactersAtCorrectPosition()
    {
        var driver = new FakeConsoleDriver(40, 10);
        driver.WriteAt(5, 2, "Hello".AsSpan(), ConsoleColor.White, ConsoleColor.Blue);

        Assert.Equal('H', driver.GetCell(5, 2).Character);
        Assert.Equal('e', driver.GetCell(6, 2).Character);
        Assert.Equal('o', driver.GetCell(9, 2).Character);
        Assert.Equal(ConsoleColor.White, driver.GetCell(5, 2).Foreground);
        Assert.Equal(ConsoleColor.Blue, driver.GetCell(5, 2).Background);
    }

    [Fact]
    public void WriteAt_ClipsTextToBufferWidth()
    {
        var driver = new FakeConsoleDriver(10, 5);
        driver.WriteAt(8, 0, "ABCDEF".AsSpan()); // only 2 chars fit

        Assert.Equal('A', driver.GetCell(8, 0).Character);
        Assert.Equal('B', driver.GetCell(9, 0).Character);
    }

    [Fact]
    public void WriteAt_IgnoresOutOfBoundsRows()
    {
        var driver = new FakeConsoleDriver(10, 5);
        // Should not throw
        driver.WriteAt(0, 99, "test".AsSpan());
        driver.WriteAt(0, -1, "test".AsSpan());
    }

    [Fact]
    public void ClearRegion_FillsWithSpaces()
    {
        var driver = new FakeConsoleDriver(20, 10);
        driver.WriteAt(2, 1, "XXXXXXXX".AsSpan());

        driver.ClearRegion(new Rect(2, 1, 8, 1));

        for (int x = 2; x < 10; x++)
            Assert.Equal(' ', driver.GetCell(x, 1).Character);
    }

    [Fact]
    public void GetSize_ReturnsConfiguredSize()
    {
        var driver = new FakeConsoleDriver(120, 40);
        var size = driver.GetSize();

        Assert.Equal(120, size.Width);
        Assert.Equal(40, size.Height);
    }

    [Fact]
    public void SetCursorPosition_UpdatesCursorCoords()
    {
        var driver = new FakeConsoleDriver();
        driver.SetCursorPosition(15, 7);

        Assert.Equal(15, driver.CursorX);
        Assert.Equal(7, driver.CursorY);
    }

    [Fact]
    public void Capture_ThenRestore_RestoresOriginalContent()
    {
        var driver = new FakeConsoleDriver(20, 10);
        driver.WriteAt(0, 0, "Hello World".AsSpan(), ConsoleColor.Yellow, ConsoleColor.DarkBlue);

        var region = new Rect(0, 0, 11, 1);
        var snapshot = driver.Capture(region);

        // Overwrite
        driver.WriteAt(0, 0, "###########".AsSpan());

        // Restore
        driver.Restore(snapshot);

        Assert.Equal('H', driver.GetCell(0, 0).Character);
        Assert.Equal('d', driver.GetCell(10, 0).Character);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(0, 0).Foreground);
        Assert.Equal(ConsoleColor.DarkBlue, driver.GetCell(0, 0).Background);
    }

    [Fact]
    public void ReadKey_ReturnsQueuedKeys()
    {
        var driver = new FakeConsoleDriver();
        var expected = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);
        driver.EnqueueKey(expected);

        var result = driver.ReadKey(true);

        Assert.Equal(expected.KeyChar, result.KeyChar);
        Assert.Equal(expected.Key, result.Key);
    }

    [Fact]
    public void TryReadInput_ReturnsFalseWhenQueueIsEmpty()
    {
        var driver = new FakeConsoleDriver();

        bool hasInput = driver.TryReadInput(intercept: true, out var input);

        Assert.False(hasInput);
        Assert.Null(input);
    }

    [Fact]
    public void TryReadInput_ReturnsQueuedInputWithoutBlocking()
    {
        var driver = new FakeConsoleDriver();
        var expected = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);
        driver.EnqueueKey(expected);

        bool hasInput = driver.TryReadInput(intercept: true, out var input);

        Assert.True(hasInput);
        var keyInput = Assert.IsType<KeyConsoleInputEvent>(input);
        Assert.Equal(expected.Key, keyInput.Key.Key);
        Assert.Equal(expected.KeyChar, keyInput.Key.KeyChar);
    }
}
