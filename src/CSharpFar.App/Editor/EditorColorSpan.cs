using CSharpFar.Console.Models;

namespace CSharpFar.App.Editor;

public readonly record struct EditorColorSpan(
    int LineIndex,
    int StartColumn,
    int Length,
    CellStyle Style)
{
    public int EndColumn => StartColumn + Length;

    public bool Contains(int lineIndex, int column) =>
        lineIndex == LineIndex &&
        column >= StartColumn &&
        column < EndColumn;
}
