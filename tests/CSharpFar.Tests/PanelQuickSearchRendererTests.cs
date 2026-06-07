using CSharpFar.App.Rendering;
using CSharpFar.Console;
using CSharpFar.Console.Models;
using CSharpFar.Tests.Fakes;

namespace CSharpFar.Tests;

public sealed class PanelQuickSearchRendererTests
{
    [Fact]
    public void Render_LongSearchTextAtEnd_ShowsBlankCellAfterLastCharacter()
    {
        var driver = new FakeConsoleDriver(width: 20, height: 8);
        var renderer = new PanelQuickSearchRenderer(new ScreenRenderer(driver));

        bool rendered = renderer.Render(new Rect(0, 0, 16, 8), "abcdefghij");

        Assert.True(rendered);
        Assert.Equal("bcdefghij ", driver.GetRegionText(new Rect(3, 5, 10, 1)));
        Assert.Equal(12, driver.CursorX);
        Assert.Equal(5, driver.CursorY);
        Assert.Equal('j', driver.GetCell(11, 5).Character);
        Assert.Equal(' ', driver.GetCell(12, 5).Character);
    }
}
