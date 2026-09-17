using CSharpFar.App.Viewer;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Tests;

public sealed class MarkdownLinkMouseTests
{
    private static readonly IReadOnlyList<LargeFileViewer.ViewerLinkHit> Hits =
    [
        new(new Rect(5, 2, 4, 1), "https://example.com"),
    ];

    [Theory]
    [InlineData(MouseKeyModifiers.None)]
    [InlineData(MouseKeyModifiers.Control)]
    public void LeftButtonUpActivatesLink(MouseKeyModifiers modifiers)
    {
        var mouse = new MouseConsoleInputEvent(
            6, 2, MouseButton.Left, MouseEventKind.Up, modifiers);

        Assert.True(LargeFileViewer.TryGetLinkTarget(mouse, Hits, out string target));
        Assert.Equal("https://example.com", target);
    }

    [Theory]
    [InlineData(MouseEventKind.Down, MouseKeyModifiers.None)]
    [InlineData(MouseEventKind.Up, MouseKeyModifiers.Shift)]
    [InlineData(MouseEventKind.Up, MouseKeyModifiers.Alt)]
    [InlineData(MouseEventKind.Up, MouseKeyModifiers.Control | MouseKeyModifiers.Shift)]
    public void OtherMouseActionsDoNotActivateLink(
        MouseEventKind kind,
        MouseKeyModifiers modifiers)
    {
        var mouse = new MouseConsoleInputEvent(
            6, 2, MouseButton.Left, kind, modifiers);

        Assert.False(LargeFileViewer.TryGetLinkTarget(mouse, Hits, out _));
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(9, 2)]
    [InlineData(6, 1)]
    [InlineData(6, 3)]
    public void ClickOutsideHalfOpenBoundsDoesNotActivateLink(int x, int y)
    {
        var mouse = new MouseConsoleInputEvent(
            x, y, MouseButton.Left, MouseEventKind.Up, MouseKeyModifiers.None);

        Assert.False(LargeFileViewer.TryGetLinkTarget(mouse, Hits, out _));
    }
}
