using CSharpFar.App.UserMenu;

namespace CSharpFar.Tests;

/// <summary>
/// Verifies Stage 18: PlaceholderExpander substitutes all placeholders correctly.
/// </summary>
public class PlaceholderExpanderTests
{
    [Fact]
    public void Current_ReplacedWithCurrentFile()
    {
        string result = PlaceholderExpander.Expand(
            "open {current}", @"C:\docs\readme.txt", [], @"C:\docs", @"C:\other");

        Assert.Equal(@"open C:\docs\readme.txt", result);
    }

    [Fact]
    public void PanelDir_ReplacedWithActiveDir()
    {
        string result = PlaceholderExpander.Expand(
            "cd {panelDir}", string.Empty, [], @"C:\projects", @"C:\other");

        Assert.Equal(@"cd C:\projects", result);
    }

    [Fact]
    public void OtherPanelDir_ReplacedWithInactiveDir()
    {
        string result = PlaceholderExpander.Expand(
            "xcopy {panelDir} {otherPanelDir}", string.Empty, [], @"C:\src", @"C:\dst");

        Assert.Equal(@"xcopy C:\src C:\dst", result);
    }

    [Fact]
    public void Selected_WithSelections_ReplacedWithQuotedList()
    {
        var selected = new[] { @"C:\a.txt", @"C:\b.txt" };
        string result = PlaceholderExpander.Expand(
            "del {selected}", @"C:\a.txt", selected, @"C:\", @"C:\");

        Assert.Equal(@"del ""C:\a.txt"" ""C:\b.txt""", result);
    }

    [Fact]
    public void Selected_WithNoSelection_FallsBackToCurrentFile()
    {
        string result = PlaceholderExpander.Expand(
            "type {selected}", @"C:\readme.txt", [], @"C:\", @"C:\");

        Assert.Equal(@"type ""C:\readme.txt""", result);
    }

    [Fact]
    public void Selected_WithNoSelectionAndNoCurrentFile_IsEmpty()
    {
        string result = PlaceholderExpander.Expand(
            "cmd {selected}", string.Empty, [], @"C:\", @"C:\");

        Assert.Equal("cmd ", result);
    }

    [Fact]
    public void MultipleOccurrences_AllReplaced()
    {
        string result = PlaceholderExpander.Expand(
            "echo {panelDir} && cd {panelDir}", string.Empty, [], @"C:\work", @"C:\other");

        Assert.Equal(@"echo C:\work && cd C:\work", result);
    }
}
