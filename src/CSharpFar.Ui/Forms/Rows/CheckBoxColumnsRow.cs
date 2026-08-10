using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class CheckBoxColumnsRow : FormRow, IFormFocusTarget, IFormCursorProvider
{
    private readonly IReadOnlyList<IReadOnlyList<CheckBoxRow>> _columns;
    private readonly int _columnGap;
    private readonly FormGridShape _shape;
    private readonly FormGridNavigationState _navigation = new();

    internal CheckBoxColumnsRow(IReadOnlyList<IReadOnlyList<CheckBoxRow>> columns, int columnGap = 2)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        if (columnGap < 0) throw new ArgumentOutOfRangeException(nameof(columnGap));

        var seen = new HashSet<CheckBoxRow>(ReferenceEqualityComparer.Instance);
        _columns = columns.Select(column =>
        {
            if (column is null || column.Count == 0) throw new ArgumentException("Columns cannot be null or empty.", nameof(columns));
            return (IReadOnlyList<CheckBoxRow>)column.Select(row =>
            {
                if (row is null || !seen.Add(row)) throw new ArgumentException("Checkbox rows must be unique and non-null.", nameof(columns));
                return row;
            }).ToArray();
        }).ToArray();
        _columnGap = columnGap;
        _shape = new FormGridShape(_columns.Select(column => column.Count).ToArray());
    }

    internal override FormRowRole Role { get; init; } = FormRowRole.Option;
    internal override bool IsFocusable => _columns.SelectMany(static column => column).Any(static row => row.IsFocusable);
    internal override int Height => _columns.Max(static column => column.Count);
    internal override int DesiredWidth => _columns.Sum(column => column.Max(row => row.DesiredWidth)) + _columnGap * (_columns.Count - 1);

    internal override void Render(FormRowRenderContext context)
    {
        context.Canvas.FillRegion(context.Bounds, FarDialogStyles.Fill);
        FormGridLayout layout = CalculateLayout(context.Bounds);
        FormGridPosition? effectivePosition = _navigation.ResolveCurrent(_shape, IsCellEnabled);
        foreach (FormGridCell cell in layout.Cells)
        {
            CheckBoxRow checkBox = _columns[cell.Column][cell.Row];
            bool focused = context.Focused && effectivePosition == cell.Position && checkBox.IsFocusable;
            checkBox.Render(new FormRowRenderContext(context.Canvas, cell.Bounds, focused));
        }
    }

    internal override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        if (!_navigation.EnsureCurrent(_shape, IsCellEnabled)) return FormInputResult.NotHandled;
        return key.Key switch
        {
            ConsoleKey.Spacebar or ConsoleKey.Enter => ToggleFocused(),
            ConsoleKey.UpArrow => Move(_navigation.MoveVertical(-1, _shape, IsCellEnabled)),
            ConsoleKey.DownArrow => Move(_navigation.MoveVertical(1, _shape, IsCellEnabled)),
            ConsoleKey.LeftArrow => Move(_navigation.MoveHorizontal(-1, _shape, IsCellEnabled)),
            ConsoleKey.RightArrow => Move(_navigation.MoveHorizontal(1, _shape, IsCellEnabled)),
            ConsoleKey.Tab when key.Modifiers.HasFlag(ConsoleModifiers.Shift) => _navigation.MoveTab(-1, _shape, IsCellEnabled),
            ConsoleKey.Tab => _navigation.MoveTab(1, _shape, IsCellEnabled),
            _ => FormInputResult.NotHandled,
        };
    }

    internal override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down) return FormInputResult.NotHandled;
        FormGridLayout layout = CalculateLayout(context.Bounds);
        if (!_navigation.SelectPointer(mouse.X, mouse.Y, layout, cell => IsCellEnabled(cell.Position)) || _navigation.Current is not { } position) return FormInputResult.NotHandled;
        CheckBoxRow checkBox = _columns[position.Column][position.Row];
        layout.TryGetCell(position, out FormGridCell cell);
        return checkBox.HandleMouse(mouse, new FormRowMouseContext(true, new FormRowLayout(cell.Bounds, null, cell.Bounds)));
    }

    bool IFormCursorProvider.TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        FormGridLayout layout = CalculateLayout(context.Bounds);
        FormGridPosition? effectivePosition = _navigation.ResolveCurrent(_shape, IsCellEnabled);
        if (!context.Focused || effectivePosition is not { } position || !layout.TryGetCell(position, out FormGridCell cell))
        {
            cursor = default;
            return false;
        }
        return ((IFormCursorProvider)_columns[position.Column][position.Row])
            .TryGetCursor(new FormRowRenderContext(context.Canvas, cell.Bounds, true), out cursor);
    }

    private FormGridLayout CalculateLayout(Rect bounds) => FormGridLayout.Calculate(_shape, bounds, _columnGap);
    private bool IsCellEnabled(FormGridPosition cell) => _columns[cell.Column][cell.Row].IsFocusable;
    private FormInputResult ToggleFocused()
    {
        FormGridPosition position = _navigation.Current!.Value;
        return _columns[position.Column][position.Row].HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), new FormRowInputContext(true));
    }
    private static FormInputResult Move(bool _) => FormInputResult.Handled;
}
