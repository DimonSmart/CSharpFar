namespace CSharpFar.App.Editor;

/// <summary>
/// In-memory text editing model. All operations keep the cursor valid.
/// </summary>
public sealed class EditorModel
{
    private readonly List<string> _lines;

    public IReadOnlyList<string> Lines => _lines;
    public int  CursorRow { get; private set; }
    public int  CursorCol { get; private set; }
    public bool IsDirty   { get; private set; }

    public EditorModel(IEnumerable<string> lines)
    {
        _lines = new List<string>(lines);
        if (_lines.Count == 0) _lines.Add("");
    }

    private string CurrLine => _lines[CursorRow];

    // ── editing ───────────────────────────────────────────────────────────────

    public void InsertChar(char ch)
    {
        _lines[CursorRow] = CurrLine[..CursorCol] + ch + CurrLine[CursorCol..];
        CursorCol++;
        IsDirty = true;
    }

    public void DeleteBack()
    {
        if (CursorCol > 0)
        {
            _lines[CursorRow] = CurrLine[..(CursorCol - 1)] + CurrLine[CursorCol..];
            CursorCol--;
            IsDirty = true;
        }
        else if (CursorRow > 0)
        {
            int newCol = _lines[CursorRow - 1].Length;
            _lines[CursorRow - 1] += CurrLine;
            _lines.RemoveAt(CursorRow);
            CursorRow--;
            CursorCol = newCol;
            IsDirty = true;
        }
    }

    public void DeleteForward()
    {
        if (CursorCol < CurrLine.Length)
        {
            _lines[CursorRow] = CurrLine[..CursorCol] + CurrLine[(CursorCol + 1)..];
            IsDirty = true;
        }
        else if (CursorRow < _lines.Count - 1)
        {
            _lines[CursorRow] = CurrLine + _lines[CursorRow + 1];
            _lines.RemoveAt(CursorRow + 1);
            IsDirty = true;
        }
    }

    public void BreakLine()
    {
        string rest = CurrLine[CursorCol..];
        _lines[CursorRow] = CurrLine[..CursorCol];
        _lines.Insert(CursorRow + 1, rest);
        CursorRow++;
        CursorCol = 0;
        IsDirty = true;
    }

    // ── cursor movement ───────────────────────────────────────────────────────

    public void MoveLeft()
    {
        if (CursorCol > 0) CursorCol--;
        else if (CursorRow > 0) { CursorRow--; CursorCol = _lines[CursorRow].Length; }
    }

    public void MoveRight()
    {
        if (CursorCol < CurrLine.Length) CursorCol++;
        else if (CursorRow < _lines.Count - 1) { CursorRow++; CursorCol = 0; }
    }

    public void MoveUp(int count = 1)
    {
        CursorRow = Math.Max(0, CursorRow - count);
        CursorCol = Math.Min(CursorCol, _lines[CursorRow].Length);
    }

    public void MoveDown(int count = 1)
    {
        CursorRow = Math.Min(_lines.Count - 1, CursorRow + count);
        CursorCol = Math.Min(CursorCol, _lines[CursorRow].Length);
    }

    public void MoveToLineStart() => CursorCol = 0;
    public void MoveToLineEnd()   => CursorCol = CurrLine.Length;

    public void MoveToDocStart() { CursorRow = 0; CursorCol = 0; }
    public void MoveToDocEnd()   { CursorRow = _lines.Count - 1; CursorCol = CurrLine.Length; }

    // ── persistence helpers ───────────────────────────────────────────────────

    /// <summary>Returns the document as a single string with the given line separator.</summary>
    public string GetText(string newLine) => string.Join(newLine, _lines);

    /// <summary>Returns the document using the platform line separator.</summary>
    public string GetText() => GetText(Environment.NewLine);

    /// <summary>Resets the dirty flag after a successful save.</summary>
    public void MarkClean() => IsDirty = false;
}
