using System.Diagnostics.CodeAnalysis;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Console;

public interface IConsoleDriver
{
    ConsoleSize GetSize();

    /// <summary>Reads the next keyboard, mouse, or resize event.</summary>
    ConsoleInputEvent ReadInput(bool intercept, CancellationToken cancellationToken = default);

    /// <summary>Reads one pending input event without blocking.</summary>
    bool TryReadInput(bool intercept, [NotNullWhen(true)] out ConsoleInputEvent? inputEvent);

    /// <summary>Key-only read; kept for dialog compatibility.</summary>
    ConsoleKeyInfo ReadKey(bool intercept);

    void WriteAt(int x, int y, ReadOnlySpan<char> text,
        ConsoleColor? foreground = null, ConsoleColor? background = null);
    void ClearRegion(Rect region);
    void SetCursorPosition(int x, int y);
    void SetCursorVisible(bool visible);
    ScreenSnapshot Capture(Rect region);
    void Restore(ScreenSnapshot snapshot);
}
