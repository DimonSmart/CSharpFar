using System.Text;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.App.Editor;

/// <summary>Renders one editor line by consuming its source scalars and styles in visual order.</summary>
internal static class EditorVisualLineRenderer
{
    public static void Render(
        IUiCanvas canvas,
        int screenX,
        int screenY,
        int width,
        string line,
        int lineIndex,
        int leftColumn,
        int tabSize,
        IReadOnlyList<EditorColorSpan> syntaxSpans,
        EditorSelection? selection,
        int cursorLine,
        int cursorColumn,
        bool customCursorVisible,
        CellStyle textStyle,
        CellStyle selectionStyle)
    {
        if (width <= 0)
            return;

        int visibleEnd = leftColumn + width;
        int logical = 0;
        int visual = 0;
        int syntaxIndex = 0;
        int outputX = screenX;
        var run = new StringBuilder(width);
        CellStyle? runStyle = null;
        int runX = screenX;

        void FlushRun()
        {
            if (run.Length == 0)
                return;

            canvas.Write(runX, screenY, run.ToString(), runStyle!.Value);
            run.Clear();
        }

        void Append(string text, int cellWidth, CellStyle style)
        {
            if (runStyle is null || runStyle.Value.Equals(style))
            {
                if (runStyle is null)
                    runX = outputX;
                runStyle = style;
                run.Append(text);
            }
            else
            {
                FlushRun();
                runX = outputX;
                runStyle = style;
                run.Append(text);
            }

            outputX += cellWidth;
        }

        while (logical < line.Length && outputX < screenX + width)
        {
            int scalarStart = logical;
            int next = EditorUnicode.NextScalarColumn(line, scalarStart);
            int cellWidth = line[scalarStart] == '\t'
                ? tabSize - visual % tabSize
                : EditorUnicode.DisplayCellWidthAt(line, scalarStart);

            // A combining mark belongs to the preceding visible scalar.  Keep it in
            // the same write so the console receives the actual Unicode sequence.
            int glyphEnd = next;
            if (cellWidth > 0 && line[scalarStart] != '\t')
            {
                while (glyphEnd < line.Length && EditorUnicode.DisplayCellWidthAt(line, glyphEnd) == 0)
                    glyphEnd = EditorUnicode.NextScalarColumn(line, glyphEnd);
            }

            if (visual + cellWidth <= leftColumn)
            {
                visual += cellWidth;
                logical = glyphEnd;
                continue;
            }

            CellStyle style = ResolveStyle(
                syntaxSpans,
                lineIndex,
                scalarStart,
                ref syntaxIndex,
                textStyle);
            if (IsSelected(selection, lineIndex, scalarStart) ||
                (customCursorVisible && lineIndex == cursorLine && scalarStart == cursorColumn && cellWidth > 1))
            {
                style = selectionStyle;
            }

            int clippedLeft = Math.Max(0, leftColumn - visual);
            int available = width - (outputX - screenX);
            int visibleWidth = Math.Min(cellWidth - clippedLeft, available);
            if (visibleWidth > 0)
            {
                bool fullyVisible = clippedLeft == 0 && visibleWidth == cellWidth;
                string text = fullyVisible && line[scalarStart] != '\t'
                    ? line[scalarStart..glyphEnd]
                    : new string(' ', visibleWidth);
                Append(text, visibleWidth, style);
            }

            visual += cellWidth;
            logical = glyphEnd;
        }

        while (outputX < screenX + width)
        {
            int visualColumn = leftColumn + outputX - screenX;
            int logicalColumn = line.Length + Math.Max(0, visualColumn - visual);
            CellStyle style = IsSelected(selection, lineIndex, logicalColumn) ? selectionStyle : textStyle;
            Append(" ", 1, style);
        }

        FlushRun();
    }

    private static CellStyle ResolveStyle(
        IReadOnlyList<EditorColorSpan> spans,
        int lineIndex,
        int logicalColumn,
        ref int spanIndex,
        CellStyle fallback)
    {
        while (spanIndex < spans.Count && spans[spanIndex].EndColumn <= logicalColumn)
            spanIndex++;

        return spanIndex < spans.Count && spans[spanIndex].Contains(lineIndex, logicalColumn)
            ? spans[spanIndex].Style
            : fallback;
    }

    private static bool IsSelected(EditorSelection? selection, int lineIndex, int logicalColumn)
    {
        if (selection is null || selection.IsEmpty)
            return false;

        if (selection.Mode == EditorSelectionMode.Rectangular)
        {
            int startLine = Math.Min(selection.Anchor.Line, selection.Active.Line);
            int endLine = Math.Max(selection.Anchor.Line, selection.Active.Line);
            int startColumn = Math.Min(selection.Anchor.Column, selection.Active.Column);
            int endColumn = Math.Max(selection.Anchor.Column, selection.Active.Column);
            return lineIndex >= startLine && lineIndex <= endLine &&
                logicalColumn >= startColumn && logicalColumn < endColumn;
        }

        var (start, end) = selection.OrderedRange;
        if (lineIndex < start.Line || lineIndex > end.Line)
            return false;
        if (start.Line == end.Line)
            return logicalColumn >= start.Column && logicalColumn < end.Column;
        if (lineIndex == start.Line)
            return logicalColumn >= start.Column;
        return lineIndex != end.Line || logicalColumn < end.Column;
    }
}
