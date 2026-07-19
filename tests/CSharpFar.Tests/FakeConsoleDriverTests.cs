using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public class FakeConsoleDriverTests
{
    [Fact]
    public void WriteAt_ClipsTextToBufferWidth()
    {
        var driver = new FakeConsoleDriver(10, 5);
        driver.WriteAt(8, 0, "ABCDEF".AsSpan());

        Assert.Equal('A', driver.GetCell(8, 0).Character);
        Assert.Equal('B', driver.GetCell(9, 0).Character);
    }

    [Fact]
    public void Capture_ThenRestore_RestoresOriginalContent()
    {
        var driver = new FakeConsoleDriver(20, 10);
        driver.WriteAt(0, 0, "Hello World".AsSpan(), ConsoleColor.Yellow, ConsoleColor.DarkBlue);

        var region = new Rect(0, 0, 11, 1);
        var snapshot = driver.Capture(region);

        driver.WriteAt(0, 0, "###########".AsSpan());

        driver.Restore(snapshot);

        Assert.Equal('H', driver.GetCell(0, 0).Character);
        Assert.Equal('d', driver.GetCell(10, 0).Character);
        Assert.Equal(ConsoleColor.Yellow, driver.GetCell(0, 0).Foreground);
        Assert.Equal(ConsoleColor.DarkBlue, driver.GetCell(0, 0).Background);
    }
}
