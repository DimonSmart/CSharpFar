using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class CheckBoxColumnsRow : FormRow, IFormCursorProvider
{
    private readonly IReadOnlyList<IReadOnlyList<CheckBoxRow>> _columns;
    private readonly int _columnGap;
    private int _focusedColumn;
    private int _focusedRow;

    public CheckBoxColumnsRow(
        IReadOnlyList<IReadOnlyList<CheckBoxRow>> columns,
        int columnGap = 2)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("At least one column is required.", nameof(columns));
        if (columnGap < 0)
            throw new ArgumentOutOfRangeException(nameof(columnGap), "Column gap cannot be negative.");

        var seen = new HashSet<CheckBoxRow>(ReferenceEqualityComparer.Instance);
        var copied = new List<IReadOnlyList<CheckBoxRow>>(columns.Count);
        foreach (IReadOnlyList<CheckBoxRow>? column in columns)
        {
            if (column is null)
                throw new ArgumentException("Columns cannot contain null entries.", nameof(columns));
            if (column.Count == 0)
                throw new ArgumentException("Columns cannot be empty.", nameof(columns));

            var copiedColumn = new CheckBoxRow[column.Count];
            for (int rowIndex = 0; rowIndex < column.Count; rowIndex++)
            {
                CheckBoxRow row = column[rowIndex] ?? throw new ArgumentException("Checkbox rows cannot be null.", nameof(columns));
                if (!seen.Add(row))
                    throw new ArgumentException("The same checkbox row cannot be used more than once.", nameof(columns));
                copiedColumn[rowIndex] = row;
            }

            copied.Add(copiedColumn);
        }

        _columns = copied;
        _columnGap = columnGap;
    }

    public override FormRowRole Role { get; init; } = FormRowRole.Option;
    public override bool IsFocusable => _columns.SelectMany(static column => column).Any(static row => row.IsFocusable);
    public override int Height => _columns.Max(static column => column.Count);

    public override void Render(FormRowRenderContext context)
    {
        context.Canvas.FillRegion(context.Bounds, FarDialogStyles.Fill);
        EnsureFocusedEnabled();

        foreach (CheckBoxCell cell in CalculateCells(context.Bounds))
        {
            bool focused = context.Focused &&
                cell.Column == _focusedColumn &&
                cell.Row == _focusedRow &&
                cell.CheckBox.IsFocusable;
            cell.CheckBox.Render(new FormRowRenderContext(context.Canvas, cell.Bounds, focused, context.CanvasHeight));
        }
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!EnsureFocusedEnabled())
            return FormInputResult.NotHandled;

        return key.Key switch
        {
            ConsoleKey.Spacebar or ConsoleKey.Enter => ToggleFocused(key),
            ConsoleKey.UpArrow => MoveVertical(-1),
            ConsoleKey.DownArrow => MoveVertical(1),
            ConsoleKey.LeftArrow => MoveHorizontal(-1),
            ConsoleKey.RightArrow => MoveHorizontal(1),
            ConsoleKey.Tab when key.Modifiers.HasFlag(ConsoleModifiers.Shift) => MoveTab(-1),
            ConsoleKey.Tab => MoveTab(1),
            _ => FormInputResult.NotHandled,
        };
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down)
            return FormInputResult.NotHandled;

        foreach (CheckBoxCell cell in CalculateCells(context.Bounds))
        {
            if (!cell.Bounds.Contains(mouse.X, mouse.Y))
                continue;
            if (!cell.CheckBox.IsFocusable)
                return FormInputResult.NotHandled;

            _focusedColumn = cell.Column;
            _focusedRow = cell.Row;
            return cell.CheckBox.HandleMouse(
                mouse,
                new FormRowMouseContext(true, context.CanvasHeight, new FormRowLayout(cell.Bounds, null, cell.Bounds)));
        }

        return FormInputResult.NotHandled;
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        cursor = default;
        if (!context.Focused || !EnsureFocusedEnabled())
            return false;

        foreach (CheckBoxCell cell in CalculateCells(context.Bounds))
        {
            if (cell.Column != _focusedColumn || cell.Row != _focusedRow)
                continue;

            return cell.CheckBox.TryGetCursor(
                new FormRowRenderContext(context.Canvas, cell.Bounds, true, context.CanvasHeight),
                out cursor);
        }

        return false;
    }

    private FormInputResult ToggleFocused(ConsoleKeyInfo key)
    {
        CheckBoxRow focused = _columns[_focusedColumn][_focusedRow];
        return focused.HandleKey(key, new FormRowInputContext(true));
    }

    private FormInputResult MoveVertical(int direction)
    {
        IReadOnlyList<CheckBoxRow> column = _columns[_focusedColumn];
        for (int row = _focusedRow + direction; row >= 0 && row < column.Count; row += direction)
        {
            if (!column[row].IsFocusable)
                continue;

            _focusedRow = row;
            return FormInputResult.Handled;
        }

        return FormInputResult.Handled;
    }

    private FormInputResult MoveHorizontal(int direction)
    {
        for (int column = _focusedColumn + direction; column >= 0 && column < _columns.Count; column += direction)
        {
            if (TryFindNearestEnabledRow(column, _focusedRow, out int row))
            {
                _focusedColumn = column;
                _focusedRow = row;
                return FormInputResult.Handled;
            }
        }

        return FormInputResult.Handled;
    }

    private FormInputResult MoveTab(int direction)
    {
        var positions = RowMajorEnabledPositions();
        int current = positions.FindIndex(position => position.Column == _focusedColumn && position.Row == _focusedRow);
        if (current < 0)
        {
            EnsureFocusedEnabled();
            return FormInputResult.Handled;
        }

        int next = current + direction;
        if (next < 0)
            return FormInputResult.MoveFocusPrevious;
        if (next >= positions.Count)
            return FormInputResult.MoveFocusNext;

        (_focusedColumn, _focusedRow) = positions[next];
        return FormInputResult.Handled;
    }

    private bool EnsureFocusedEnabled()
    {
        if (IsFocusedPositionEnabled())
            return true;

        foreach ((int column, int row) in RowMajorEnabledPositions())
        {
            _focusedColumn = column;
            _focusedRow = row;
            return true;
        }

        return false;
    }

    private bool IsFocusedPositionEnabled() =>
        _focusedColumn >= 0 &&
        _focusedColumn < _columns.Count &&
        _focusedRow >= 0 &&
        _focusedRow < _columns[_focusedColumn].Count &&
        _columns[_focusedColumn][_focusedRow].IsFocusable;

    private bool TryFindNearestEnabledRow(int column, int preferredRow, out int row)
    {
        IReadOnlyList<CheckBoxRow> rows = _columns[column];
        int clamped = Math.Clamp(preferredRow, 0, rows.Count - 1);
        for (int distance = 0; distance < rows.Count; distance++)
        {
            int up = clamped - distance;
            if (up >= 0 && rows[up].IsFocusable)
            {
                row = up;
                return true;
            }

            int down = clamped + distance;
            if (distance > 0 && down < rows.Count && rows[down].IsFocusable)
            {
                row = down;
                return true;
            }
        }

        row = -1;
        return false;
    }

    private List<(int Column, int Row)> RowMajorEnabledPositions()
    {
        var positions = new List<(int Column, int Row)>();
        for (int row = 0; row < Height; row++)
        {
            for (int column = 0; column < _columns.Count; column++)
            {
                if (row < _columns[column].Count && _columns[column][row].IsFocusable)
                    positions.Add((column, row));
            }
        }

        return positions;
    }

    private IReadOnlyList<CheckBoxCell> CalculateCells(Rect bounds)
    {
        int columnCount = _columns.Count;
        int gap = columnCount <= 1 ? 0 : Math.Min(_columnGap, bounds.Width / (columnCount - 1));
        int totalGap = (columnCount - 1) * gap;
        int availableWidth = Math.Max(0, bounds.Width - totalGap);
        int baseColumnWidth = availableWidth / columnCount;
        int remainder = availableWidth % columnCount;
        int x = bounds.X;
        var cells = new List<CheckBoxCell>();

        for (int column = 0; column < columnCount; column++)
        {
            int columnWidth = baseColumnWidth + (column < remainder ? 1 : 0);
            for (int row = 0; row < _columns[column].Count; row++)
            {
                cells.Add(new CheckBoxCell(
                    column,
                    row,
                    _columns[column][row],
                    new Rect(x, bounds.Y + row, columnWidth, 1)));
            }

            x += columnWidth + gap;
        }

        return cells;
    }

    private readonly record struct CheckBoxCell(int Column, int Row, CheckBoxRow CheckBox, Rect Bounds);
}
