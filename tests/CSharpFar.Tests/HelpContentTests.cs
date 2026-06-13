using CSharpFar.App.Viewer;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 19: HelpContent contains the expected key-binding entries.
/// </summary>
public class HelpContentTests
{
    [Theory]
    [InlineData("F1")]
    [InlineData("F3")]
    [InlineData("F4")]
    [InlineData("F5")]
    [InlineData("F7")]
    [InlineData("F8")]
    [InlineData("F10")]
    [InlineData("Ctrl+O")]
    [InlineData("Ctrl+F1")]
    [InlineData("Ctrl+F2")]
    [InlineData("Ctrl+Q")]
    [InlineData("Alt+F7")]
    [InlineData("Alt+F8")]
    [InlineData("Alt+F11")]
    [InlineData("Alt+F12")]
    public void Lines_ContainsKeyBinding(string keyText)
    {
        Assert.Contains(HelpContent.Lines, l => l.FullText.Contains(keyText, StringComparison.OrdinalIgnoreCase));
    }

}
