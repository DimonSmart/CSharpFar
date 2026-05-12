namespace CSharpFar.Core.Models;

public sealed class CommandLineState
{
    private readonly List<char> _buffer = new();

    public string Text => new(_buffer.ToArray());
    public int CursorPosition { get; private set; }
    public bool HasText => _buffer.Count > 0;

    public void Insert(char ch)
    {
        _buffer.Insert(CursorPosition, ch);
        CursorPosition++;
    }

    public void InsertText(string text)
    {
        _buffer.InsertRange(CursorPosition, text);
        CursorPosition += text.Length;
    }

    /// <summary>Backspace — deletes the character to the left of the cursor.</summary>
    public void DeleteBack()
    {
        if (CursorPosition > 0)
        {
            _buffer.RemoveAt(CursorPosition - 1);
            CursorPosition--;
        }
    }

    /// <summary>Delete — deletes the character to the right of the cursor.</summary>
    public void DeleteForward()
    {
        if (CursorPosition < _buffer.Count)
            _buffer.RemoveAt(CursorPosition);
    }

    public void MoveCursor(int delta) =>
        CursorPosition = Math.Clamp(CursorPosition + delta, 0, _buffer.Count);

    public void MoveToStart() => CursorPosition = 0;
    public void MoveToEnd()   => CursorPosition = _buffer.Count;

    public void Clear()
    {
        _buffer.Clear();
        CursorPosition = 0;
    }

    /// <summary>Replaces the buffer with <paramref name="text"/> and moves cursor to end.</summary>
    public void SetText(string text)
    {
        _buffer.Clear();
        _buffer.AddRange(text);
        CursorPosition = _buffer.Count;
    }
}
