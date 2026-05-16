namespace CSharpFar.Core.Models;

public sealed class CommandLineState
{
    private readonly List<char> _buffer = new();

    public string Text => new(_buffer.ToArray());
    public int CursorPosition { get; private set; }
    public bool HasText => _buffer.Count > 0;

    // ── Selection ─────────────────────────────────────────────────────────────
    /// <summary>Start of the selection, or <c>null</c> when nothing is selected.</summary>
    public int? SelectionStart { get; private set; }
    /// <summary>Number of selected characters (valid only when <see cref="SelectionStart"/> is not null).</summary>
    public int SelectionLength { get; private set; }
    public bool HasSelection => SelectionStart.HasValue && SelectionLength > 0;

    /// <summary>Returns the currently selected text, or <c>null</c> when nothing is selected.</summary>
    public string? SelectedText =>
        HasSelection ? new string(_buffer.GetRange(SelectionStart!.Value, SelectionLength).ToArray()) : null;

    /// <summary>Selects all text and moves the cursor to the end.</summary>
    public void SelectAll()
    {
        if (_buffer.Count == 0)
        {
            ClearSelection();
            return;
        }

        SelectionStart  = 0;
        SelectionLength = _buffer.Count;
        CursorPosition  = _buffer.Count;
    }

    /// <summary>Clears the selection without changing the cursor or buffer.</summary>
    public void ClearSelection()
    {
        SelectionStart  = null;
        SelectionLength = 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    /// <summary>Deletes the selected range and clears the selection. Cursor moves to selection start.</summary>
    private void DeleteSelection()
    {
        if (!HasSelection) return;
        _buffer.RemoveRange(SelectionStart!.Value, SelectionLength);
        CursorPosition = SelectionStart.Value;
        ClearSelection();
    }

    // ── Editing ───────────────────────────────────────────────────────────────
    public void Insert(char ch)
    {
        if (HasSelection) DeleteSelection();
        _buffer.Insert(CursorPosition, ch);
        CursorPosition++;
    }

    public void InsertText(string text)
    {
        if (HasSelection) DeleteSelection();
        _buffer.InsertRange(CursorPosition, text);
        CursorPosition += text.Length;
    }

    /// <summary>Backspace — deletes the selection or the character to the left of the cursor.</summary>
    public void DeleteBack()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (CursorPosition > 0)
        {
            _buffer.RemoveAt(CursorPosition - 1);
            CursorPosition--;
        }
    }

    /// <summary>Delete — deletes the selection or the character to the right of the cursor.</summary>
    public void DeleteForward()
    {
        if (HasSelection) { DeleteSelection(); return; }
        if (CursorPosition < _buffer.Count)
            _buffer.RemoveAt(CursorPosition);
    }

    public void MoveCursor(int delta)
    {
        ClearSelection();
        CursorPosition = Math.Clamp(CursorPosition + delta, 0, _buffer.Count);
    }

    public void MoveToStart() { ClearSelection(); CursorPosition = 0; }
    public void MoveToEnd()   { ClearSelection(); CursorPosition = _buffer.Count; }

    /// <summary>
    /// Moves the cursor to <paramref name="newPosition"/> while extending or shrinking the selection.
    /// Equivalent to holding Shift while pressing an arrow/Home/End key.
    /// </summary>
    public void MoveCursorWithSelection(int newPosition)
    {
        newPosition = Math.Clamp(newPosition, 0, _buffer.Count);
        if (newPosition == CursorPosition)
            return;

        if (!HasSelection)
        {
            // Start a fresh selection anchored at the current cursor
            int anchor = CursorPosition;
            CursorPosition = newPosition;
            SelectionStart  = Math.Min(anchor, newPosition);
            SelectionLength = Math.Abs(newPosition - anchor);
        }
        else
        {
            // Existing selection — determine the anchor (the end that did NOT move last time)
            int anchor = CursorPosition == SelectionStart!.Value
                ? SelectionStart.Value + SelectionLength   // cursor was at start → anchor is end
                : SelectionStart.Value;                    // cursor was at end   → anchor is start

            CursorPosition  = newPosition;
            SelectionStart  = Math.Min(anchor, newPosition);
            SelectionLength = Math.Abs(newPosition - anchor);
        }
    }

    public void Clear()
    {
        _buffer.Clear();
        CursorPosition = 0;
        ClearSelection();
    }

    /// <summary>Replaces the buffer with <paramref name="text"/> and moves cursor to end.</summary>
    public void SetText(string text)
    {
        _buffer.Clear();
        _buffer.AddRange(text);
        CursorPosition = _buffer.Count;
        ClearSelection();
    }
}
