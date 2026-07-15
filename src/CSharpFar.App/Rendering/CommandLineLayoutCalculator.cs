using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal static class CommandLineLayoutCalculator
{
    public static CommandLineLayout Calculate(
        int row,
        int width,
        string currentDirectory,
        CommandLineState state)
    {
        int safeWidth = Math.Max(0, width);
        int promptLength = currentDirectory.Length + 1;
        int fullLength = promptLength + state.Text.Length;
        int displayOffset = GetDisplayOffset(
            safeWidth,
            promptLength,
            fullLength,
            state.CursorPosition);
        int cursorX = promptLength + state.CursorPosition - displayOffset;
        UiCursorPlacement? cursor = cursorX >= 0 && cursorX < safeWidth
            ? new UiCursorPlacement(cursorX, row)
            : null;

        return new CommandLineLayout(
            new Rect(0, row, safeWidth, safeWidth > 0 ? 1 : 0),
            promptLength,
            displayOffset,
            state.Text.Length,
            cursorX,
            cursor);
    }

    private static int GetDisplayOffset(
        int totalWidth,
        int promptLength,
        int fullLength,
        int cursorPosition)
    {
        if (totalWidth <= 0 || fullLength < totalWidth)
            return 0;

        int rawCursorX = promptLength + cursorPosition;
        int maxOffset = Math.Max(0, fullLength - totalWidth + 1);
        return Math.Clamp(rawCursorX - totalWidth + 1, 0, maxOffset);
    }
}

internal readonly record struct CommandLineLayout(
    Rect Bounds,
    int PromptLength,
    int DisplayOffset,
    int TextLength,
    int CursorX,
    UiCursorPlacement? Cursor)
{
    public int TextPositionFromX(int x)
    {
        if (Bounds.Width <= 0)
            return 0;

        int clampedX = Math.Clamp(x, Bounds.X, Bounds.Right - 1);
        return Math.Clamp(clampedX + DisplayOffset - PromptLength, 0, TextLength);
    }
}
