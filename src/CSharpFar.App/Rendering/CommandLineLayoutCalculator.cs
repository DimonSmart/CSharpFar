using CSharpFar.Console.Models;
using CSharpFar.Core.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Rendering;

internal static class CommandLineLayoutCalculator
{
    public static ApplicationCommandLineFrame Calculate(
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

        return new ApplicationCommandLineFrame(
            new Rect(0, row, safeWidth, safeWidth > 0 ? 1 : 0),
            promptLength,
            displayOffset,
            state.Text.Length,
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
