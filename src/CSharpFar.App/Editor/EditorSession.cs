using CSharpFar.Core.Models;

namespace CSharpFar.App.Editor;

public sealed class EditorSession
{
    private readonly AppSettings.EditorSettings _settings;
    private readonly HashSet<int> _bookmarks = [];
    private readonly EditorPosition?[] _numberedBookmarks = new EditorPosition?[10];

    public EditorSession(
        string filePath,
        EditorDocument document,
        AppSettings.EditorSettings settings,
        bool readOnly)
    {
        FilePath = filePath;
        Document = document;
        _settings = settings;
        ReadOnly = readOnly;
        UndoHistory = new EditorUndoHistory(EditorSettingsResolver.ResolveUndoSize(settings));
    }

    public string FilePath { get; }
    public EditorDocument Document { get; }
    public EditorViewport Viewport { get; } = new();
    public EditorPosition Cursor { get; private set; } = EditorPosition.Start;
    public EditorSelection? Selection { get; private set; }
    public bool ReadOnly { get; }
    public EditorUndoHistory UndoHistory { get; }
    public IReadOnlyCollection<int> Bookmarks => _bookmarks;
    public IReadOnlyDictionary<int, EditorPosition> NumberedBookmarks =>
        _numberedBookmarks
            .Select((position, index) => (position, index))
            .Where(item => item.position is not null)
            .ToDictionary(item => item.index, item => item.position!.Value);

    public event EventHandler<EditorSessionEventArgs>? Opened;
    public event EventHandler<EditorInputEventArgs>? Input;
    public event EventHandler<EditorSessionEventArgs>? TextChanged;
    public event EventHandler<EditorSessionEventArgs>? Saved;
    public event EventHandler<EditorSessionEventArgs>? Closed;
    public event EventHandler<EditorRedrawEventArgs>? Redraw;

    public void RaiseOpened() => Opened?.Invoke(this, new EditorSessionEventArgs(this));
    public void RaiseInput(ConsoleKeyInfo key) => Input?.Invoke(this, new EditorInputEventArgs(this, key));
    public void RaiseSaved() => Saved?.Invoke(this, new EditorSessionEventArgs(this));
    public void RaiseClosed() => Closed?.Invoke(this, new EditorSessionEventArgs(this));
    public void RaiseRedraw(int firstLine, int lineCount) =>
        Redraw?.Invoke(this, new EditorRedrawEventArgs(this, firstLine, lineCount));

    public void MoveLeft(bool extendSelection = false, bool preserveSelection = false)
    {
        var next = Cursor.Column > 0
            ? Cursor with { Column = Cursor.Column - 1 }
            : Cursor.Line > 0
                ? new EditorPosition(Cursor.Line - 1, Document.Buffer.GetLine(Cursor.Line - 1).Length)
                : Cursor;
        MoveCursor(next, extendSelection, preserveSelection);
    }

    public void MoveRight(bool extendSelection = false, bool preserveSelection = false)
    {
        MoveCursor(Cursor with { Column = Cursor.Column + 1 }, extendSelection, preserveSelection);
    }

    public void MoveUp(int count = 1, bool extendSelection = false, bool preserveSelection = false) =>
        MoveCursor(new EditorPosition(Math.Max(0, Cursor.Line - count), Cursor.Column), extendSelection, preserveSelection);

    public void MoveDown(int count = 1, bool extendSelection = false, bool preserveSelection = false) =>
        MoveCursor(new EditorPosition(Math.Min(Document.Buffer.LineCount - 1, Cursor.Line + count), Cursor.Column), extendSelection, preserveSelection);

    public void MoveToLineStart(bool extendSelection = false, bool preserveSelection = false) =>
        MoveCursor(Cursor with { Column = 0 }, extendSelection, preserveSelection);

    public void MoveToLineEnd(bool extendSelection = false, bool preserveSelection = false) =>
        MoveCursor(Cursor with { Column = Document.Buffer.GetLine(Cursor.Line).Length }, extendSelection, preserveSelection);

    public void MoveToDocumentStart(bool extendSelection = false, bool preserveSelection = false) =>
        MoveCursor(EditorPosition.Start, extendSelection, preserveSelection);

    public void MoveToDocumentEnd(bool extendSelection = false, bool preserveSelection = false) =>
        MoveCursor(Document.Buffer.End, extendSelection, preserveSelection);

    public void MoveTo(EditorPosition position, bool extendSelection = false, bool preserveSelection = false) =>
        MoveCursor(position, extendSelection, preserveSelection);

    public void SelectRange(EditorPosition start, EditorPosition end, EditorSelectionMode mode = EditorSelectionMode.Linear)
    {
        Selection = new EditorSelection(start, end, mode);
        Cursor = NormalizeCursorPosition(end);
    }

    public void SelectAll() =>
        SelectRange(EditorPosition.Start, Document.Buffer.End);

    public void ClearSelection() => Selection = null;

    public void MoveWordLeft(bool extendSelection = false, bool preserveSelection = false)
    {
        int offset = PositionToOffset(Cursor);
        if (offset == 0)
            return;

        MoveCursor(OffsetToPosition(PreviousWordStartOffset(offset)), extendSelection, preserveSelection);
    }

    public void MoveWordRight(bool extendSelection = false, bool preserveSelection = false)
    {
        int offset = PositionToOffset(Cursor);
        MoveCursor(OffsetToPosition(NextWordStartOffset(offset)), extendSelection, preserveSelection);
    }

    public bool InsertText(string text, string transactionName = "Insert")
    {
        if (ReadOnly || text.Length == 0)
            return false;

        DeleteSelectionIfNeeded(transactionName, out _);
        string currentLine = Document.Buffer.GetLine(Cursor.Line);
        int realColumn = Math.Min(Cursor.Column, currentLine.Length);
        string virtualPadding = Cursor.Column > currentLine.Length
            ? new string(' ', Cursor.Column - currentLine.Length)
            : string.Empty;
        EditorPosition start = Cursor with { Column = realColumn };
        EditorPosition end = start;
        string oldText = string.Empty;
        string insertedText = virtualPadding + text;
        EditorPosition after = NormalizeCursorPosition(EditorTextBuffer.Advance(start, insertedText));
        ApplyChange(transactionName, start, end, oldText, insertedText, after);
        return true;
    }

    public bool DeleteBack()
    {
        if (ReadOnly)
            return false;
        if (DeleteSelectionIfNeeded("Delete", out bool deletedSelection))
            return deletedSelection;
        if (Cursor == EditorPosition.Start)
            return false;

        string line = Document.Buffer.GetLine(Cursor.Line);
        if (Cursor.Column > line.Length)
        {
            MoveCursor(Cursor with { Column = Cursor.Column - 1 }, extendSelection: false);
            return true;
        }

        EditorPosition start = Cursor.Column > 0
            ? Cursor with { Column = Cursor.Column - 1 }
            : new EditorPosition(Cursor.Line - 1, Document.Buffer.GetLine(Cursor.Line - 1).Length);
        string oldText = Cursor.Column > 0 ? Document.Buffer.GetLine(Cursor.Line)[start.Column..Cursor.Column] : "\n";
        ApplyChange("Backspace", start, Cursor, oldText, string.Empty, start);
        return true;
    }

    public bool DeleteForward()
    {
        if (ReadOnly)
            return false;
        if (DeleteSelectionIfNeeded("Delete", out bool deletedSelection))
            return deletedSelection;
        if (Cursor == Document.Buffer.End)
            return false;

        EditorPosition end;
        string oldText;
        string line = Document.Buffer.GetLine(Cursor.Line);
        if (Cursor.Column > line.Length)
            return false;

        if (Cursor.Column < line.Length)
        {
            end = Cursor with { Column = Cursor.Column + 1 };
            oldText = line[Cursor.Column..(Cursor.Column + 1)];
        }
        else
        {
            end = new EditorPosition(Cursor.Line + 1, 0);
            oldText = "\n";
        }

        ApplyChange("Delete", Cursor, end, oldText, string.Empty, Cursor);
        return true;
    }

    public bool DeleteSelection()
    {
        if (ReadOnly)
            return false;

        return DeleteSelectionIfNeeded("Delete block", out bool deleted) && deleted;
    }

    public bool DeleteSelectionOrCurrentLine()
    {
        if (ReadOnly)
            return false;
        if (DeleteSelectionIfNeeded("Delete block", out bool deletedSelection))
            return deletedSelection;

        return DeleteCurrentLine();
    }

    public bool DeleteCurrentLine()
    {
        if (ReadOnly)
            return false;

        Selection = null;
        int lineIndex = Cursor.Line;
        string lineText = Document.Buffer.GetLine(lineIndex);
        if (Document.Buffer.LineCount == 1)
        {
            if (lineText.Length == 0)
                return false;

            var start = new EditorPosition(0, 0);
            var end = new EditorPosition(0, lineText.Length);
            ApplyChange("Delete line", start, end, lineText, string.Empty, start);
            return true;
        }

        if (lineIndex < Document.Buffer.LineCount - 1)
        {
            var start = new EditorPosition(lineIndex, 0);
            var end = new EditorPosition(lineIndex + 1, 0);
            string oldText = Document.Buffer.GetTextInRange(start, end);
            ApplyChange("Delete line", start, end, oldText, string.Empty, start);
            return true;
        }

        int previousLine = lineIndex - 1;
        var previousLineEnd = new EditorPosition(previousLine, Document.Buffer.GetLine(previousLine).Length);
        var documentEnd = Document.Buffer.End;
        string removedText = Document.Buffer.GetTextInRange(previousLineEnd, documentEnd);
        ApplyChange("Delete line", previousLineEnd, documentEnd, removedText, string.Empty, new EditorPosition(previousLine, 0));
        return true;
    }

    public bool DeleteToLineEnd()
    {
        if (ReadOnly)
            return false;

        Selection = null;
        string line = Document.Buffer.GetLine(Cursor.Line);
        int startColumn = Math.Min(Cursor.Column, line.Length);
        if (startColumn >= line.Length)
            return false;

        var start = new EditorPosition(Cursor.Line, startColumn);
        var end = new EditorPosition(Cursor.Line, line.Length);
        string oldText = line[startColumn..];
        ApplyChange("Delete to line end", start, end, oldText, string.Empty, start);
        return true;
    }

    public bool DeleteWordLeft()
    {
        if (ReadOnly)
            return false;

        Selection = null;
        var cursor = Document.Buffer.NormalizePosition(Cursor);
        int offset = PositionToOffset(cursor);
        if (offset == 0)
        {
            MoveCursor(cursor, extendSelection: false);
            return false;
        }

        int startOffset = PreviousWordStartOffset(offset);
        var start = OffsetToPosition(startOffset);
        string oldText = Document.Buffer.GetTextInRange(start, cursor);
        if (oldText.Length == 0)
            return false;

        ApplyChange("Delete previous word", start, cursor, oldText, string.Empty, start);
        return true;
    }

    public bool DeleteWordRight()
    {
        if (ReadOnly)
            return false;

        Selection = null;
        var cursor = Document.Buffer.NormalizePosition(Cursor);
        int offset = PositionToOffset(cursor);
        int endOffset = NextWordStartOffset(offset);
        if (endOffset == offset)
            return false;

        var end = OffsetToPosition(endOffset);
        string oldText = Document.Buffer.GetTextInRange(cursor, end);
        if (oldText.Length == 0)
            return false;

        ApplyChange("Delete next word", cursor, end, oldText, string.Empty, cursor);
        return true;
    }

    public bool BreakLine() => InsertText("\n", "Line split");

    public bool Undo()
    {
        var transaction = UndoHistory.PopUndo();
        if (transaction is null)
            return false;

        foreach (var change in transaction.Changes.OrderBy(change => change.Start.Line).ThenBy(change => change.Start.Column))
        {
            var inverse = new EditorTextChange(
                change.Start,
                EditorTextBuffer.Advance(change.Start, change.NewText),
                change.NewText,
                change.OldText);
            Document.Buffer.Replace(inverse.Start, inverse.End, inverse.NewText, Document.Format.LineEnding);
        }

        Document.RestoreRevision(transaction.BeforeRevision);
        Cursor = NormalizeCursorPosition(transaction.BeforeCursor);
        Selection = transaction.BeforeSelection;
        TextChanged?.Invoke(this, new EditorSessionEventArgs(this));
        return true;
    }

    public bool Redo()
    {
        var transaction = UndoHistory.PopRedo();
        if (transaction is null)
            return false;

        foreach (var change in transaction.Changes)
            Document.Buffer.Replace(change.Start, change.End, change.NewText, Document.Format.LineEnding);

        Document.RestoreRevision(transaction.AfterRevision);
        Cursor = NormalizeCursorPosition(transaction.AfterCursor);
        Selection = transaction.AfterSelection;
        TextChanged?.Invoke(this, new EditorSessionEventArgs(this));
        return true;
    }

    public void SetSelectionMode(EditorSelectionMode mode)
    {
        Selection = new EditorSelection(Cursor, Cursor, mode);
    }

    public string CopySelection()
    {
        if (Selection is null || Selection.IsEmpty)
            return string.Empty;

        return Selection.Mode == EditorSelectionMode.Rectangular
            ? CopyRectangularSelection()
            : Document.Buffer.GetTextInRange(Selection.OrderedRange.Start, Selection.OrderedRange.End);
    }

    public bool CutSelection()
    {
        string text = CopySelection();
        if (text.Length == 0)
            return false;
        return DeleteSelectionIfNeeded("Cut", out _);
    }

    public void PasteText(string text) => InsertText(text, "Paste");

    public bool CopySelectionToCursor()
    {
        if (ReadOnly || Selection is null || Selection.IsEmpty)
            return false;

        string selectedText = CopySelection();
        if (selectedText.Length == 0)
            return false;

        string currentLine = Document.Buffer.GetLine(Cursor.Line);
        int realColumn = Math.Min(Cursor.Column, currentLine.Length);
        string virtualPadding = Cursor.Column > currentLine.Length
            ? new string(' ', Cursor.Column - currentLine.Length)
            : string.Empty;
        var start = Cursor with { Column = realColumn };
        string insertedText = virtualPadding + selectedText;
        ApplyChange(
            "Copy block",
            start,
            start,
            string.Empty,
            insertedText,
            NormalizeCursorPosition(EditorTextBuffer.Advance(start, insertedText)));
        return true;
    }

    public bool MoveSelectionToCursor()
    {
        if (ReadOnly || Selection is null || Selection.IsEmpty || Selection.Mode == EditorSelectionMode.Rectangular)
            return false;

        var (start, end) = Selection.OrderedRange;
        var target = Document.Buffer.NormalizePosition(Cursor);
        if (EditorTextBuffer.Compare(start, target) <= 0 && EditorTextBuffer.Compare(target, end) <= 0)
            return false;

        string oldText = FlattenText();
        int startOffset = PositionToOffset(start);
        int endOffset = PositionToOffset(end);
        int targetOffset = PositionToOffset(target);
        string selectedText = oldText[startOffset..endOffset];
        string withoutSelection = oldText.Remove(startOffset, selectedText.Length);
        int adjustedTargetOffset = targetOffset > endOffset
            ? targetOffset - selectedText.Length
            : targetOffset;
        string newText = withoutSelection.Insert(adjustedTargetOffset, selectedText);
        var afterCursor = PositionInText(newText, adjustedTargetOffset + selectedText.Length);
        var changes = new[]
        {
            new EditorTextChange(start, end, selectedText, string.Empty),
            new EditorTextChange(target, target, string.Empty, selectedText),
        };
        ApplyChanges("Move block", changes, afterCursor, afterSelection: null);
        return true;
    }

    public bool DuplicateSelectionOrCurrentLine()
    {
        if (ReadOnly)
            return false;

        if (Selection is { IsEmpty: false, Mode: EditorSelectionMode.Rectangular })
            return false;

        if (Selection is { IsEmpty: false })
        {
            var (_, end) = Selection.OrderedRange;
            string selectedText = CopySelection();
            if (selectedText.Length == 0)
                return false;

            ApplyChange(
                "Duplicate selection",
                end,
                end,
                string.Empty,
                selectedText,
                EditorTextBuffer.Advance(end, selectedText));
            return true;
        }

        string lineText = Document.Buffer.GetLine(Cursor.Line);
        if (Cursor.Line < Document.Buffer.LineCount - 1)
        {
            var insertAt = new EditorPosition(Cursor.Line + 1, 0);
            ApplyChange(
                "Duplicate line",
                insertAt,
                insertAt,
                string.Empty,
                lineText + "\n",
                new EditorPosition(Cursor.Line + 1, Math.Min(Cursor.Column, lineText.Length)));
            return true;
        }

        var documentEnd = Document.Buffer.End;
        ApplyChange(
            "Duplicate line",
            documentEnd,
            documentEnd,
            string.Empty,
            "\n" + lineText,
            new EditorPosition(Cursor.Line + 1, Math.Min(Cursor.Column, lineText.Length)));
        return true;
    }

    public bool ConvertSelectionToUppercase() =>
        ConvertSelectionCase(toUppercase: true);

    public bool ConvertSelectionToLowercase() =>
        ConvertSelectionCase(toUppercase: false);

    public bool ShiftSelectedLinesLeftOrCurrentLine()
    {
        if (ReadOnly)
            return false;

        var (startLine, endLine) = SelectedLineRangeOrCurrentLine();
        var changes = new List<EditorTextChange>();
        for (int line = endLine; line >= startLine; line--)
        {
            string text = Document.Buffer.GetLine(line);
            if (text.Length == 0 || text[0] is not (' ' or '\t'))
                continue;

            var start = new EditorPosition(line, 0);
            var end = new EditorPosition(line, 1);
            changes.Add(new EditorTextChange(start, end, text[..1], string.Empty));
        }

        if (changes.Count == 0)
            return false;

        ApplyChanges("Shift block left", changes, Cursor, Selection);
        return true;
    }

    public bool ShiftSelectedLinesRightOrCurrentLine()
    {
        if (ReadOnly)
            return false;

        var (startLine, endLine) = SelectedLineRangeOrCurrentLine();
        var changes = new List<EditorTextChange>();
        for (int line = endLine; line >= startLine; line--)
        {
            var start = new EditorPosition(line, 0);
            changes.Add(new EditorTextChange(start, start, string.Empty, " "));
        }

        ApplyChanges("Shift block right", changes, Cursor, Selection);
        return true;
    }

    public void ToggleBookmark()
    {
        if (!_bookmarks.Add(Cursor.Line))
            _bookmarks.Remove(Cursor.Line);
    }

    public bool MoveToNextBookmark()
    {
        int? next = _bookmarks.Where(line => line > Cursor.Line).Order().Cast<int?>().FirstOrDefault()
            ?? _bookmarks.Order().Cast<int?>().FirstOrDefault();
        if (next is null)
            return false;

        MoveCursor(new EditorPosition(next.Value, 0), extendSelection: false);
        return true;
    }

    public bool MoveToPreviousBookmark()
    {
        int? previous = _bookmarks.Where(line => line < Cursor.Line).OrderDescending().Cast<int?>().FirstOrDefault()
            ?? _bookmarks.OrderDescending().Cast<int?>().FirstOrDefault();
        if (previous is null)
            return false;

        MoveCursor(new EditorPosition(previous.Value, 0), extendSelection: false);
        return true;
    }

    public void SetNumberedBookmark(int slot)
    {
        ValidateBookmarkSlot(slot);
        _numberedBookmarks[slot] = Cursor;
    }

    public bool MoveToNumberedBookmark(int slot)
    {
        ValidateBookmarkSlot(slot);
        var bookmark = _numberedBookmarks[slot];
        if (bookmark is null)
            return false;

        MoveCursor(bookmark.Value, extendSelection: false);
        return true;
    }

    public EditorSearchMatch? Find(EditorSearchOptions options)
    {
        var service = new EditorSearchService(_settings.WordDiv);
        return service.Find(this, options);
    }

    public bool Replace(EditorSearchOptions options, string replacement)
    {
        var match = Find(options);
        if (match is null)
            return false;

        string oldText = Document.Buffer.GetTextInRange(match.Value.Start, match.Value.End);
        ApplyChange("Replace", match.Value.Start, match.Value.End, oldText, replacement,
            EditorTextBuffer.Advance(match.Value.Start, replacement));
        return true;
    }

    public int ReplaceAll(EditorSearchOptions options, string replacement)
    {
        var service = new EditorSearchService(_settings.WordDiv);
        IReadOnlyList<EditorSearchMatch> matches = service.FindAll(this, options);
        if (matches.Count == 0 || ReadOnly)
            return 0;

        string oldText = Document.Buffer.GetText(Document.Format.WithLineEnding(EditorLineEnding.Lf));
        for (int index = matches.Count - 1; index >= 0; index--)
        {
            var match = matches[index];
            Document.Buffer.Replace(match.Start, match.End, replacement, Document.Format.LineEnding);
        }

        Document.RestoreRevision(Document.Revision + 1);
        string newText = Document.Buffer.GetText(Document.Format.WithLineEnding(EditorLineEnding.Lf));
        UndoHistory.Record(new EditorTransaction(
            "Replace all",
            new EditorTextChange(EditorPosition.Start, EditorTextBuffer.Advance(EditorPosition.Start, oldText), oldText, newText),
            Cursor,
            Cursor,
            Selection,
            Selection)
        {
            BeforeRevision = Document.Revision - 1,
            AfterRevision = Document.Revision,
        });
        TextChanged?.Invoke(this, new EditorSessionEventArgs(this));
        return matches.Count;
    }

    public string FlattenText() => Document.Buffer.GetText(Document.Format.WithLineEnding(EditorLineEnding.Lf));

    public int PositionToOffset(EditorPosition position)
    {
        position = Document.Buffer.NormalizePosition(position);
        int offset = 0;
        for (int line = 0; line < position.Line; line++)
            offset += Document.Buffer.GetLine(line).Length + 1;
        return offset + position.Column;
    }

    public EditorPosition OffsetToPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, FlattenText().Length);
        int remaining = offset;
        for (int line = 0; line < Document.Buffer.LineCount; line++)
        {
            int lineLength = Document.Buffer.GetLine(line).Length;
            if (remaining <= lineLength)
                return new EditorPosition(line, remaining);
            remaining -= lineLength + 1;
        }

        return Document.Buffer.End;
    }

    private bool ConvertSelectionCase(bool toUppercase)
    {
        if (ReadOnly || Selection is null || Selection.IsEmpty)
            return false;

        return Selection.Mode == EditorSelectionMode.Rectangular
            ? ConvertRectangularSelectionCase(toUppercase)
            : ConvertLinearSelectionCase(toUppercase);
    }

    private bool ConvertLinearSelectionCase(bool toUppercase)
    {
        if (Selection is null)
            return false;

        var (start, end) = Selection.OrderedRange;
        string oldText = Document.Buffer.GetTextInRange(start, end);
        string newText = ConvertCase(oldText, toUppercase);
        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return false;

        ApplyChange(
            toUppercase ? "Uppercase selection" : "Lowercase selection",
            start,
            end,
            oldText,
            newText,
            end);
        return true;
    }

    private bool ConvertRectangularSelectionCase(bool toUppercase)
    {
        if (Selection is null)
            return false;

        int startLine = Math.Min(Selection.Anchor.Line, Selection.Active.Line);
        int endLine = Math.Max(Selection.Anchor.Line, Selection.Active.Line);
        int startColumn = Math.Min(Selection.Anchor.Column, Selection.Active.Column);
        int endColumn = Math.Max(Selection.Anchor.Column, Selection.Active.Column);
        if (startColumn == endColumn)
            return false;

        var changes = new List<EditorTextChange>();
        for (int line = endLine; line >= startLine; line--)
        {
            string text = Document.Buffer.GetLine(line);
            if (startColumn >= text.Length)
                continue;

            var start = new EditorPosition(line, startColumn);
            var end = new EditorPosition(line, Math.Min(endColumn, text.Length));
            string oldText = text[start.Column..end.Column];
            string newText = ConvertCase(oldText, toUppercase);
            if (!string.Equals(oldText, newText, StringComparison.Ordinal))
                changes.Add(new EditorTextChange(start, end, oldText, newText));
        }

        if (changes.Count == 0)
            return false;

        ApplyChanges(
            toUppercase ? "Uppercase rectangular selection" : "Lowercase rectangular selection",
            changes,
            Selection.Active,
            Selection);
        return true;
    }

    private static string ConvertCase(string text, bool toUppercase) =>
        toUppercase ? text.ToUpperInvariant() : text.ToLowerInvariant();

    private (int StartLine, int EndLine) SelectedLineRangeOrCurrentLine()
    {
        if (Selection is null || Selection.IsEmpty)
            return (Cursor.Line, Cursor.Line);

        int startLine = Math.Min(Selection.Anchor.Line, Selection.Active.Line);
        int endLine = Math.Max(Selection.Anchor.Line, Selection.Active.Line);
        return (startLine, endLine);
    }

    private static EditorPosition PositionInText(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int line = 0;
        int column = 0;
        for (int index = 0; index < offset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 0;
            }
            else
            {
                column++;
            }
        }

        return new EditorPosition(line, column);
    }

    private bool DeleteSelectionIfNeeded(string transactionName, out bool deleted)
    {
        deleted = false;
        if (Selection is null || Selection.IsEmpty)
            return false;

        if (Selection.Mode == EditorSelectionMode.Rectangular)
            return DeleteRectangularSelection(transactionName, out deleted);

        var (start, end) = Selection.OrderedRange;
        string oldText = Document.Buffer.GetTextInRange(start, end);
        ApplyChange(transactionName, start, end, oldText, string.Empty, start);
        Selection = null;
        deleted = true;
        return true;
    }

    private bool DeleteRectangularSelection(string transactionName, out bool deleted)
    {
        deleted = false;
        if (Selection is null)
            return false;

        int startLine = Math.Min(Selection.Anchor.Line, Selection.Active.Line);
        int endLine = Math.Max(Selection.Anchor.Line, Selection.Active.Line);
        int startColumn = Math.Min(Selection.Anchor.Column, Selection.Active.Column);
        int endColumn = Math.Max(Selection.Anchor.Column, Selection.Active.Column);
        if (startColumn == endColumn)
            return false;

        var changes = new List<EditorTextChange>();
        for (int line = endLine; line >= startLine; line--)
        {
            string text = Document.Buffer.GetLine(line);
            if (startColumn >= text.Length)
                continue;

            var start = new EditorPosition(line, startColumn);
            var end = new EditorPosition(line, Math.Min(endColumn, text.Length));
            string oldText = text[start.Column..end.Column];
            changes.Add(new EditorTextChange(start, end, oldText, string.Empty));
        }

        if (changes.Count == 0)
            return false;

        ApplyChanges(
            transactionName,
            changes,
            new EditorPosition(startLine, startColumn),
            afterSelection: null);
        deleted = true;
        return true;
    }

    private string CopyRectangularSelection()
    {
        if (Selection is null)
            return string.Empty;

        int startLine = Math.Min(Selection.Anchor.Line, Selection.Active.Line);
        int endLine = Math.Max(Selection.Anchor.Line, Selection.Active.Line);
        int startColumn = Math.Min(Selection.Anchor.Column, Selection.Active.Column);
        int endColumn = Math.Max(Selection.Anchor.Column, Selection.Active.Column);
        var lines = new List<string>();
        for (int line = startLine; line <= endLine; line++)
        {
            string text = Document.Buffer.GetLine(line);
            lines.Add(startColumn >= text.Length
                ? string.Empty
                : text[startColumn..Math.Min(endColumn, text.Length)]);
        }

        return string.Join('\n', lines);
    }

    private void MoveCursor(EditorPosition position, bool extendSelection, bool preserveSelection = false)
    {
        var normalized = NormalizeCursorPosition(position);
        if (extendSelection)
        {
            Selection = Selection is null
                ? new EditorSelection(Cursor, normalized, EditorSelectionMode.Linear)
                : Selection with { Active = normalized };
        }
        else if (!preserveSelection)
        {
            Selection = null;
        }

        Cursor = normalized;
    }

    private EditorPosition NormalizeCursorPosition(EditorPosition position)
    {
        int line = Math.Clamp(position.Line, 0, Document.Buffer.LineCount - 1);
        int column = Math.Max(0, position.Column);
        return new EditorPosition(line, column);
    }

    private void ApplyChange(
        string transactionName,
        EditorPosition start,
        EditorPosition end,
        string oldText,
        string newText,
        EditorPosition afterCursor)
    {
        var beforeCursor = Cursor;
        var beforeSelection = Selection;
        var change = new EditorTextChange(start, end, oldText, newText);
        long beforeRevision = Document.Revision;
        Document.Replace(change, Document.Format.LineEnding);
        Cursor = NormalizeCursorPosition(afterCursor);
        Selection = null;
        var transaction = new EditorTransaction(
            transactionName,
            change,
            beforeCursor,
            Cursor,
            beforeSelection,
            Selection)
        {
            BeforeRevision = beforeRevision,
            AfterRevision = Document.Revision,
        };
        UndoHistory.Record(transaction);
        ShiftBookmarksAfterChange(start.Line, newText.Count(ch => ch == '\n') - oldText.Count(ch => ch == '\n'));
        TextChanged?.Invoke(this, new EditorSessionEventArgs(this));
    }

    private void ApplyChanges(
        string transactionName,
        IReadOnlyList<EditorTextChange> changes,
        EditorPosition afterCursor,
        EditorSelection? afterSelection)
    {
        if (changes.Count == 0)
            return;

        var orderedChanges = changes
            .OrderByDescending(change => change.Start.Line)
            .ThenByDescending(change => change.Start.Column)
            .ToArray();
        var beforeCursor = Cursor;
        var beforeSelection = Selection;
        long beforeRevision = Document.Revision;
        foreach (var change in orderedChanges)
            Document.Buffer.Replace(change.Start, change.End, change.NewText, Document.Format.LineEnding);

        Document.RestoreRevision(Document.Revision + 1);
        Cursor = NormalizeCursorPosition(afterCursor);
        Selection = afterSelection;
        var transaction = new EditorTransaction(
            transactionName,
            orderedChanges,
            beforeCursor,
            Cursor,
            beforeSelection,
            Selection)
        {
            BeforeRevision = beforeRevision,
            AfterRevision = Document.Revision,
        };
        UndoHistory.Record(transaction);
        int firstChangedLine = orderedChanges.Min(change => change.Start.Line);
        int lineDelta = orderedChanges.Sum(change => change.NewText.Count(ch => ch == '\n') - change.OldText.Count(ch => ch == '\n'));
        ShiftBookmarksAfterChange(firstChangedLine, lineDelta);
        TextChanged?.Invoke(this, new EditorSessionEventArgs(this));
    }

    private void ShiftBookmarksAfterChange(int startLine, int lineDelta)
    {
        if (lineDelta == 0)
            return;

        if (_bookmarks.Count > 0)
        {
            var shifted = _bookmarks
                .Select(line => line > startLine ? Math.Max(0, line + lineDelta) : line)
                .ToHashSet();
            _bookmarks.Clear();
            foreach (int line in shifted)
                _bookmarks.Add(Math.Min(line, Document.Buffer.LineCount - 1));
        }

        for (int slot = 0; slot < _numberedBookmarks.Length; slot++)
        {
            var bookmark = _numberedBookmarks[slot];
            if (bookmark is null)
                continue;

            int line = bookmark.Value.Line > startLine
                ? Math.Max(0, bookmark.Value.Line + lineDelta)
                : bookmark.Value.Line;
            line = Math.Min(line, Document.Buffer.LineCount - 1);
            int column = Math.Min(bookmark.Value.Column, Document.Buffer.GetLine(line).Length);
            _numberedBookmarks[slot] = new EditorPosition(line, column);
        }
    }

    private static void ValidateBookmarkSlot(int slot)
    {
        if (slot is < 0 or > 9)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Bookmark slot must be between 0 and 9.");
    }

    private int PreviousWordStartOffset(int offset)
    {
        var text = FlattenText();
        offset = Math.Clamp(offset, 0, text.Length);
        if (offset == 0)
            return 0;

        offset--;
        while (offset > 0 && IsWordDiv(text[offset]))
            offset--;
        while (offset > 0 && !IsWordDiv(text[offset - 1]))
            offset--;
        return offset;
    }

    private int NextWordStartOffset(int offset)
    {
        var text = FlattenText();
        offset = Math.Clamp(offset, 0, text.Length);
        while (offset < text.Length && !IsWordDiv(text[offset]))
            offset++;
        while (offset < text.Length && IsWordDiv(text[offset]))
            offset++;
        return offset;
    }

    private bool IsWordDiv(char ch) => _settings.WordDiv.IndexOf(ch, StringComparison.Ordinal) >= 0;
}
