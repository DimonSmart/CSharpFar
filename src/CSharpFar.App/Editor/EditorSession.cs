using CSharpFar.Core.Models;

namespace CSharpFar.App.Editor;

public sealed class EditorSession
{
    private readonly AppSettings.EditorSettings _settings;
    private readonly HashSet<int> _bookmarks = [];

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

    public void MoveLeft(bool extendSelection = false)
    {
        var next = Cursor.Column > 0
            ? Cursor with { Column = Cursor.Column - 1 }
            : Cursor.Line > 0
                ? new EditorPosition(Cursor.Line - 1, Document.Buffer.GetLine(Cursor.Line - 1).Length)
                : Cursor;
        MoveCursor(next, extendSelection);
    }

    public void MoveRight(bool extendSelection = false)
    {
        MoveCursor(Cursor with { Column = Cursor.Column + 1 }, extendSelection);
    }

    public void MoveUp(int count = 1, bool extendSelection = false) =>
        MoveCursor(new EditorPosition(Math.Max(0, Cursor.Line - count), Cursor.Column), extendSelection);

    public void MoveDown(int count = 1, bool extendSelection = false) =>
        MoveCursor(new EditorPosition(Math.Min(Document.Buffer.LineCount - 1, Cursor.Line + count), Cursor.Column), extendSelection);

    public void MoveToLineStart(bool extendSelection = false) =>
        MoveCursor(Cursor with { Column = 0 }, extendSelection);

    public void MoveToLineEnd(bool extendSelection = false) =>
        MoveCursor(Cursor with { Column = Document.Buffer.GetLine(Cursor.Line).Length }, extendSelection);

    public void MoveToDocumentStart(bool extendSelection = false) =>
        MoveCursor(EditorPosition.Start, extendSelection);

    public void MoveToDocumentEnd(bool extendSelection = false) =>
        MoveCursor(Document.Buffer.End, extendSelection);

    public void MoveTo(EditorPosition position, bool extendSelection = false) =>
        MoveCursor(position, extendSelection);

    public void SelectRange(EditorPosition start, EditorPosition end, EditorSelectionMode mode = EditorSelectionMode.Linear)
    {
        Selection = new EditorSelection(start, end, mode);
        Cursor = NormalizeCursorPosition(end);
    }

    public void MoveWordLeft(bool extendSelection = false)
    {
        var text = FlattenText();
        int offset = PositionToOffset(Cursor);
        if (offset == 0)
            return;

        offset--;
        while (offset > 0 && IsWordDiv(text[offset]))
            offset--;
        while (offset > 0 && !IsWordDiv(text[offset - 1]))
            offset--;
        MoveCursor(OffsetToPosition(offset), extendSelection);
    }

    public void MoveWordRight(bool extendSelection = false)
    {
        var text = FlattenText();
        int offset = PositionToOffset(Cursor);
        while (offset < text.Length && !IsWordDiv(text[offset]))
            offset++;
        while (offset < text.Length && IsWordDiv(text[offset]))
            offset++;
        MoveCursor(OffsetToPosition(offset), extendSelection);
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

    public bool BreakLine() => InsertText("\n", "Line split");

    public bool Undo()
    {
        var transaction = UndoHistory.PopUndo();
        if (transaction is null)
            return false;

        var inverse = new EditorTextChange(
            transaction.Change.Start,
            EditorTextBuffer.Advance(transaction.Change.Start, transaction.Change.NewText),
            transaction.Change.NewText,
            transaction.Change.OldText);
        Document.Buffer.Replace(inverse.Start, inverse.End, inverse.NewText, Document.Format.LineEnding);
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

        Document.Buffer.Replace(
            transaction.Change.Start,
            transaction.Change.End,
            transaction.Change.NewText,
            Document.Format.LineEnding);
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

        for (int line = endLine; line >= startLine; line--)
        {
            string text = Document.Buffer.GetLine(line);
            if (startColumn >= text.Length)
                continue;

            var start = new EditorPosition(line, startColumn);
            var end = new EditorPosition(line, Math.Min(endColumn, text.Length));
            Document.Buffer.Replace(start, end, string.Empty, Document.Format.LineEnding);
            deleted = true;
        }

        if (!deleted)
            return false;

        Document.RestoreRevision(Document.Revision + 1);
        Cursor = new EditorPosition(startLine, startColumn);
        Selection = null;
        UndoHistory.Record(new EditorTransaction(
            transactionName,
            new EditorTextChange(Cursor, Cursor, string.Empty, string.Empty),
            Cursor,
            Cursor,
            null,
            null)
        {
            BeforeRevision = Document.Revision - 1,
            AfterRevision = Document.Revision,
        });
        TextChanged?.Invoke(this, new EditorSessionEventArgs(this));
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

    private void MoveCursor(EditorPosition position, bool extendSelection)
    {
        var normalized = NormalizeCursorPosition(position);
        if (extendSelection)
        {
            Selection = Selection is null
                ? new EditorSelection(Cursor, normalized, EditorSelectionMode.Linear)
                : Selection with { Active = normalized };
        }
        else
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

    private void ShiftBookmarksAfterChange(int startLine, int lineDelta)
    {
        if (lineDelta == 0 || _bookmarks.Count == 0)
            return;

        var shifted = _bookmarks
            .Select(line => line > startLine ? Math.Max(0, line + lineDelta) : line)
            .ToHashSet();
        _bookmarks.Clear();
        foreach (int line in shifted)
            _bookmarks.Add(Math.Min(line, Document.Buffer.LineCount - 1));
    }

    private bool IsWordDiv(char ch) => _settings.WordDiv.IndexOf(ch, StringComparison.Ordinal) >= 0;
}
