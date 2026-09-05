namespace CSharpFar.Ui;

/// <summary>Mutable editing state for a reusable single-line text input.</summary>
public sealed class SingleLineTextEditState
{
    private readonly List<char> _buffer = new();

    public string Text => new(_buffer.ToArray());
    public int CursorPosition { get; private set; }
    public bool HasText => _buffer.Count > 0;
    public int? SelectionStart { get; private set; }
    public int SelectionLength { get; private set; }
    public bool HasSelection => SelectionStart.HasValue && SelectionLength > 0;
    public string? SelectedText => HasSelection ? new string(_buffer.GetRange(SelectionStart!.Value, SelectionLength).ToArray()) : null;

    public void SelectAll()
    {
        if (_buffer.Count == 0) { ClearSelection(); return; }
        SelectionStart = 0;
        SelectionLength = _buffer.Count;
        CursorPosition = _buffer.Count;
    }

    public void ClearSelection() { SelectionStart = null; SelectionLength = 0; }

    public void Insert(char ch)
    {
        DeleteSelection();
        _buffer.Insert(CursorPosition, ch);
        CursorPosition++;
    }

    public void InsertText(string text)
    {
        text = NormalizeLineEndings(text);
        DeleteSelection();
        _buffer.InsertRange(CursorPosition, text);
        CursorPosition += text.Length;
    }

    public void DeleteBack()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (CursorPosition > 0) { _buffer.RemoveAt(CursorPosition - 1); CursorPosition--; }
    }

    public void DeleteForward()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (CursorPosition < _buffer.Count) _buffer.RemoveAt(CursorPosition);
    }

    public void MoveCursor(int delta) => MoveCursorTo(CursorPosition + delta);
    public void MoveToPreviousWord() => MoveCursorTo(PreviousWordPosition(CursorPosition));
    public void MoveToNextWord() => MoveCursorTo(NextWordPosition(CursorPosition));
    public void MoveToStart() => MoveCursorTo(0);
    public void MoveToEnd() => MoveCursorTo(_buffer.Count);

    public void MoveCursorTo(int position)
    {
        ClearSelection();
        CursorPosition = Math.Clamp(position, 0, _buffer.Count);
    }

    public void MoveCursorWithSelection(int newPosition)
    {
        newPosition = Math.Clamp(newPosition, 0, _buffer.Count);
        if (newPosition == CursorPosition) return;
        if (!HasSelection)
        {
            int anchor = CursorPosition;
            CursorPosition = newPosition;
            SelectionStart = Math.Min(anchor, newPosition);
            SelectionLength = Math.Abs(newPosition - anchor);
            return;
        }

        int existingAnchor = CursorPosition == SelectionStart!.Value
            ? SelectionStart.Value + SelectionLength
            : SelectionStart.Value;
        CursorPosition = newPosition;
        SelectionStart = Math.Min(existingAnchor, newPosition);
        SelectionLength = Math.Abs(newPosition - existingAnchor);
    }

    public void MoveToPreviousWordWithSelection() => MoveCursorWithSelection(PreviousWordPosition(CursorPosition));
    public void MoveToNextWordWithSelection() => MoveCursorWithSelection(NextWordPosition(CursorPosition));

    public void Clear() { _buffer.Clear(); CursorPosition = 0; ClearSelection(); }

    public void SetText(string text)
    {
        _buffer.Clear();
        _buffer.AddRange(NormalizeLineEndings(text));
        CursorPosition = _buffer.Count;
        ClearSelection();
    }

    private void DeleteSelection()
    {
        if (!HasSelection) return;
        _buffer.RemoveRange(SelectionStart!.Value, SelectionLength);
        CursorPosition = SelectionStart.Value;
        ClearSelection();
    }

    private int PreviousWordPosition(int position)
    {
        position = Math.Clamp(position, 0, _buffer.Count);
        while (position > 0 && char.IsWhiteSpace(_buffer[position - 1])) position--;
        while (position > 0 && !char.IsWhiteSpace(_buffer[position - 1])) position--;
        return position;
    }

    private int NextWordPosition(int position)
    {
        position = Math.Clamp(position, 0, _buffer.Count);
        while (position < _buffer.Count && !char.IsWhiteSpace(_buffer[position])) position++;
        while (position < _buffer.Count && char.IsWhiteSpace(_buffer[position])) position++;
        return position;
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
}
