using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed class CheckBoxColumnsRow : FormRow, IFormCursorProvider
{
    private readonly IReadOnlyList<IReadOnlyList<CheckBoxRow>> _columns;
    private readonly int _columnGap;
    private readonly FormGridNavigationState _navigation = new();
    private FormGridLayout? _layout;

    public CheckBoxColumnsRow(IReadOnlyList<IReadOnlyList<CheckBoxRow>> columns, int columnGap = 2)
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
    }

    public override FormRowRole Role { get; init; } = FormRowRole.Option;
    public override bool IsFocusable => _columns.SelectMany(static column => column).Any(static row => row.IsFocusable);
    public override int Height => _columns.Max(static column => column.Count);

    public override void Render(FormRowRenderContext context)
    {
        context.Canvas.FillRegion(context.Bounds, FarDialogStyles.Fill);
        FormGridLayout layout = CalculateLayout(context.Bounds);
        _layout = layout;
        _navigation.EnsureCurrent(layout, IsCellEnabled);
        foreach (FormGridCell cell in layout.Cells)
        {
            CheckBoxRow checkBox = _columns[cell.Column][cell.Row];
            bool focused = context.Focused && _navigation.Current == cell.Position && checkBox.IsFocusable;
            checkBox.Render(new FormRowRenderContext(context.Canvas, cell.Bounds, focused, context.CanvasHeight));
        }
    }

    public override FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context)
    {
        FormGridLayout layout = _layout ?? CalculateLayout(new Rect(0, 0, _columns.Count, Height));
        if (!_navigation.EnsureCurrent(layout, IsCellEnabled)) return FormInputResult.NotHandled;
        return key.Key switch
        {
            ConsoleKey.Spacebar or ConsoleKey.Enter => ToggleFocused(),
            ConsoleKey.UpArrow => Move(_navigation.MoveVertical(-1, layout, IsCellEnabled)),
            ConsoleKey.DownArrow => Move(_navigation.MoveVertical(1, layout, IsCellEnabled)),
            ConsoleKey.LeftArrow => Move(_navigation.MoveHorizontal(-1, layout, IsCellEnabled)),
            ConsoleKey.RightArrow => Move(_navigation.MoveHorizontal(1, layout, IsCellEnabled)),
            ConsoleKey.Tab when key.Modifiers.HasFlag(ConsoleModifiers.Shift) => _navigation.MoveTab(-1, layout, IsCellEnabled),
            ConsoleKey.Tab => _navigation.MoveTab(1, layout, IsCellEnabled),
            _ => FormInputResult.NotHandled,
        };
    }

    public override FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context)
    {
        if (mouse.Button != MouseButton.Left || mouse.Kind != MouseEventKind.Down) return FormInputResult.NotHandled;
        FormGridLayout layout = CalculateLayout(context.Bounds);
        _layout = layout;
        if (!_navigation.SelectPointer(mouse.X, mouse.Y, layout, IsCellEnabled) || _navigation.Current is not { } position) return FormInputResult.NotHandled;
        CheckBoxRow checkBox = _columns[position.Column][position.Row];
        layout.TryGetCell(position, out FormGridCell cell);
        return checkBox.HandleMouse(mouse, new FormRowMouseContext(true, context.CanvasHeight, new FormRowLayout(cell.Bounds, null, cell.Bounds)));
    }

    public bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor)
    {
        FormGridLayout layout = CalculateLayout(context.Bounds);
        if (!context.Focused || !_navigation.EnsureCurrent(layout, IsCellEnabled) || _navigation.Current is not { } position || !layout.TryGetCell(position, out FormGridCell cell))
        {
            cursor = default;
            return false;
        }
        return _columns[position.Column][position.Row].TryGetCursor(new FormRowRenderContext(context.Canvas, cell.Bounds, true, context.CanvasHeight), out cursor);
    }

    private FormGridLayout CalculateLayout(Rect bounds) => FormGridLayout.Calculate(bounds, _columns.Select(column => column.Count).ToArray(), _columnGap);
    private bool IsCellEnabled(FormGridCell cell) => _columns[cell.Column][cell.Row].IsFocusable;
    private FormInputResult ToggleFocused()
    {
        FormGridPosition position = _navigation.Current!.Value;
        return _columns[position.Column][position.Row].HandleKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), new FormRowInputContext(true));
    }
    private static FormInputResult Move(bool _) => FormInputResult.Handled;
}
