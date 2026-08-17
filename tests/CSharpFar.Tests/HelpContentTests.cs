using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 19: HelpContent contains the expected key-binding entries.
/// </summary>
public class HelpContentTests
{
    [Fact]
    public void Pages_ExposeIndependentMainAndCopyDocuments()
    {
        HelpPage main = HelpContent.GetPage(HelpTopic.Main);
        HelpPage copy = HelpContent.GetPage(HelpTopic.Copy);

        Assert.NotSame(main.Lines, copy.Lines);
        Assert.Equal("CSharpFar — Console Dual-Panel File Manager", main.Lines[0].Description);
        Assert.Equal("CSharpFar — Copy", copy.Lines[0].Description);
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Destination", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("* and ?", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Use template", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("{name}", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("{ext}", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Normal", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Reliable", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Fast salvage", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Ask", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Overwrite", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Only newer", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Access", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Preserve all timestamps", StringComparison.Ordinal));
        Assert.Contains(copy.Lines, line => line.FullText.Contains("Filter mask", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(copy.Lines, line => line.FullText.Contains("PANEL NAVIGATION", StringComparison.Ordinal));
        Assert.DoesNotContain(copy.Lines, line => line.FullText.Contains("IN EDITOR", StringComparison.Ordinal));
        Assert.DoesNotContain(copy.Lines, line => line.FullText.Contains("CONFIGURATION", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F3")]
    [InlineData("F4")]
    [InlineData("F5")]
    [InlineData("F7")]
    [InlineData("F8")]
    [InlineData("F10")]
    [InlineData("Ctrl+O")]
    [InlineData("Ctrl+Q")]
    [InlineData("Alt+F7")]
    [InlineData("Alt+F8")]
    [InlineData("Alt+F11")]
    [InlineData("Alt+F12")]
    public void Lines_ContainsKeyBinding(string keyText)
    {
        Assert.Contains(HelpContent.Lines, l => l.FullText.Contains(keyText, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Ctrl+F1")]
    [InlineData("Ctrl+F2")]
    [InlineData("Toggle left panel")]
    [InlineData("Toggle right panel")]
    public void Lines_DoesNotContainIndependentPanelVisibilityCommands(string text)
    {
        Assert.DoesNotContain(HelpContent.Lines, l => l.FullText.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

}
