using CSharpFar.Console.Models;

namespace CSharpFar.Console;

/// <summary>
/// Optional console-driver capability for writing a rectangular group of cells
/// while confirming that the visible viewport did not change.
/// </summary>
public interface IConsoleFrameWriter
{
    ConsoleFrameWriteCapabilities Capabilities { get; }

    bool TryWriteCellsAtViewport(
        ConsoleViewport viewport,
        int x,
        int y,
        int width,
        int height,
        ReadOnlySpan<ConsoleOutputCell> cells);

    bool TryWriteDirtyCellsAtViewport(
        ConsoleViewport viewport,
        ReadOnlySpan<ConsoleOutputRun> runs,
        ReadOnlySpan<ConsoleOutputCell> cells);
}

public readonly record struct ConsoleOutputRun(int X, int Y, int Offset, int Length);

[Flags]
public enum ConsoleFrameWriteCapabilities
{
    None = 0,
    WindowsCells = 1,
    VirtualTerminalCells = 2,
}
