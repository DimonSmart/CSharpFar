using CSharpFar.Console.Models;

namespace CSharpFar.Console;

public interface IConsoleDriver
{
    ConsoleSize GetSize();
    ConsoleKeyInfo ReadKey(bool intercept);
    void WriteAt(int x, int y, ReadOnlySpan<char> text,
        ConsoleColor? foreground = null, ConsoleColor? background = null);
    void ClearRegion(Rect region);
    void SetCursorPosition(int x, int y);
    void SetCursorVisible(bool visible);
    ScreenSnapshot Capture(Rect region);
    void Restore(ScreenSnapshot snapshot);
}
