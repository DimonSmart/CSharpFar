using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public interface IFormRow
{
    bool IsFocusable { get; }
    int Height { get; }
    void Render(FormRowRenderContext context);
    bool HandleKey(ConsoleKeyInfo key);
    bool HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context);
}

public sealed class FormRowRenderContext
{
    public FormRowRenderContext(ScreenRenderer screen, Rect bounds, bool focused)
    {
        Screen = screen;
        Bounds = bounds;
        Focused = focused;
    }

    public ScreenRenderer Screen { get; }
    public Rect Bounds { get; }
    public bool Focused { get; }
}

public sealed class FormRowMouseContext
{
    public FormRowMouseContext(Rect bounds)
    {
        Bounds = bounds;
    }

    public Rect Bounds { get; }
}

public abstract class FormRow : IFormRow
{
    public virtual bool IsFocusable => false;
    public virtual int Height => 1;
    public abstract void Render(FormRowRenderContext context);
    public virtual bool HandleKey(ConsoleKeyInfo key) => false;
    public virtual bool HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) => false;
}

public sealed class LabelRow : FormRow
{
    private readonly string _text;
    private readonly CellStyle _style;

    public LabelRow(string text, CellStyle style)
    {
        _text = text;
        _style = style;
    }

    public override void Render(FormRowRenderContext context) =>
        context.Screen.Write(context.Bounds.X, context.Bounds.Y, ScrollableFormDialog.Fit(_text, context.Bounds.Width), _style);
}

public sealed class SeparatorRow : FormRow
{
    private readonly CellStyle _style;

    public SeparatorRow(CellStyle style)
    {
        _style = style;
    }

    public override void Render(FormRowRenderContext context)
    {
        if (context.Bounds.Width <= 0)
            return;

        context.Screen.Write(
            context.Bounds.X,
            context.Bounds.Y,
            new string('─', context.Bounds.Width),
            _style);
    }
}

public sealed class ButtonRow : FormRow
{
    public override bool IsFocusable => true;
    public override void Render(FormRowRenderContext context)
    {
    }
}

public sealed class TextInputRow : FormRow
{
    private readonly CommandLineState _buffer;
    private readonly SingleLineTextHistoryState? _history;

    public TextInputRow(CommandLineState buffer, SingleLineTextHistoryState? history = null)
    {
        _buffer = buffer;
        _history = history;
    }

    public override bool IsFocusable => true;

    public override void Render(FormRowRenderContext context) =>
        SingleLineTextInput.Render(
            context.Screen,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            _buffer,
            context.Focused ? FarDialogStyles.FocusedInput : FarDialogStyles.Input,
            FarDialogStyles.Input,
            _history,
            renderDropdown: false);

    public override bool HandleKey(ConsoleKeyInfo key)
    {
        string? error = null;
        SingleLineTextInput.HandleKey(_buffer, key, ref error, _history, availableDropdownContentRows: 0);
        return true;
    }
}

public sealed class CheckBoxRow : FormRow
{
    private readonly CheckBoxLine _checkBox;

    public CheckBoxRow(CheckBoxLine checkBox)
    {
        _checkBox = checkBox;
    }

    public override bool IsFocusable => true;

    public override void Render(FormRowRenderContext context) =>
        _checkBox.Render(context.Screen, context.Bounds.X, context.Bounds.Y, context.Bounds.Width, context.Focused);

    public override bool HandleKey(ConsoleKeyInfo key) => _checkBox.TryHandleKey(key);

    public override bool HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) =>
        _checkBox.TryHandleMouse(mouse);
}

public sealed class ChoiceFormRow<T> : FormRow
{
    private readonly ChoiceRow<T> _choice;
    private readonly string _label;

    public ChoiceFormRow(ChoiceRow<T> choice, string label)
    {
        _choice = choice;
        _label = label;
    }

    public override bool IsFocusable => true;

    public override void Render(FormRowRenderContext context) =>
        _choice.RenderSegmented(
            context.Screen,
            context.Bounds.X,
            context.Bounds.Y,
            context.Bounds.Width,
            _label,
            context.Focused,
            FarDialogStyles.Fill,
            FarDialogStyles.FocusedInput);

    public override bool HandleKey(ConsoleKeyInfo key) => _choice.TryHandleKey(key);

    public override bool HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context) =>
        _choice.TryHandleMouse(mouse);
}

public sealed class ScrollableFormDialog
{
    private readonly IReadOnlyList<IFormRow>? _rows;
    private readonly int _focusRowCount;
    private readonly Func<int, int> _focusRowToVirtualRow;
    private readonly Func<int, bool> _isFocusableRow;

    public ScrollableFormDialog(
        int bodyRowCount,
        int focusRowCount,
        Func<int, int> focusRowToVirtualRow,
        Func<int, bool>? isFocusableRow = null)
    {
        if (bodyRowCount < 0)
            throw new ArgumentOutOfRangeException(nameof(bodyRowCount));
        if (focusRowCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(focusRowCount));

        BodyRowCount = bodyRowCount;
        _focusRowCount = focusRowCount;
        _focusRowToVirtualRow = focusRowToVirtualRow;
        _isFocusableRow = isFocusableRow ?? (_ => true);
    }

    public ScrollableFormDialog(IReadOnlyList<IFormRow> rows)
        : this(
            rows.Sum(row => Math.Max(1, row.Height)),
            rows.Count(row => row.IsFocusable),
            focusRow => FocusRowToVirtualRow(rows, focusRow),
            focusRow => IsFocusableRow(rows, focusRow))
    {
        _rows = rows;
    }

    public int BodyRowCount { get; }
    public int FocusRow { get; private set; }
    public int BodyScrollTop { get; private set; }
    public ScrollBarDragState? ScrollbarDrag { get; private set; }

    public void SetFocusRow(int focusRow, int viewportRows)
    {
        FocusRow = ClampFocusRow(focusRow);
        if (!_isFocusableRow(FocusRow))
            FocusRow = NextFocusableRow(FocusRow, 1);
        EnsureFocusVisible(viewportRows);
    }

    public void MoveFocus(int delta, int viewportRows)
    {
        if (delta == 0)
            return;

        FocusRow = NextFocusableRow(FocusRow, delta);
        EnsureFocusVisible(viewportRows);
    }

    public void EnsureFocusVisible(int viewportRows)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        BodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(
            BodyScrollTop,
            BodyRowCount,
            clampedViewportRows);

        int focusVirtualRow = _focusRowToVirtualRow(FocusRow);
        if (focusVirtualRow >= 0)
        {
            BodyScrollTop = ScrollStateCalculator.EnsureIndexVisible(
                focusVirtualRow,
                BodyScrollTop,
                clampedViewportRows);
        }

        BodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(
            BodyScrollTop,
            BodyRowCount,
            clampedViewportRows);
    }

    public bool TryHandleWheel(MouseConsoleInputEvent mouse, int viewportRows, int wheelRows = 3)
    {
        if (mouse.Kind != MouseEventKind.Wheel)
            return false;

        int delta = mouse.Button switch
        {
            MouseButton.WheelUp => -Math.Abs(wheelRows),
            MouseButton.WheelDown => Math.Abs(wheelRows),
            _ => 0,
        };
        if (delta == 0)
            return false;

        BodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(
            BodyScrollTop + delta,
            BodyRowCount,
            Math.Max(1, viewportRows));
        return true;
    }

    public bool TryHitTestBodyRow(Rect contentBounds, int viewportRows, int x, int y, out int virtualRow)
    {
        return TryHitTestBodyRow(contentBounds, BodyScrollTop, BodyRowCount, viewportRows, x, y, out virtualRow);
    }

    public bool TryHitTestFocusRow(Rect contentBounds, int viewportRows, int x, int y, out int focusRow)
    {
        focusRow = -1;
        if (_rows is null || !TryHitTestBodyRow(contentBounds, viewportRows, x, y, out int virtualRow))
            return false;

        int currentVirtualRow = 0;
        int currentFocusRow = 0;
        foreach (var row in _rows)
        {
            int height = Math.Max(1, row.Height);
            if (virtualRow >= currentVirtualRow && virtualRow < currentVirtualRow + height)
            {
                if (!row.IsFocusable)
                    return false;

                focusRow = currentFocusRow;
                return true;
            }

            if (row.IsFocusable)
                currentFocusRow++;
            currentVirtualRow += height;
        }

        return false;
    }

    public bool TryHandleScrollbarMouse(MouseConsoleInputEvent mouse, Rect scrollbarBounds, int viewportRows)
    {
        int firstVisibleIndex = BodyScrollTop;
        var dragState = ScrollbarDrag;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                mouse,
                scrollbarBounds,
                BodyRowCount,
                Math.Max(1, viewportRows),
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        BodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            BodyRowCount,
            Math.Max(1, viewportRows));
        ScrollbarDrag = dragState;
        return true;
    }

    private int NextFocusableRow(int start, int delta)
    {
        int step = delta < 0 ? -1 : 1;
        int row = ClampFocusRow(start);
        for (int i = 0; i < _focusRowCount; i++)
        {
            row = (row + step + _focusRowCount) % _focusRowCount;
            if (_isFocusableRow(row))
                return row;
        }

        return ClampFocusRow(start);
    }

    private int ClampFocusRow(int focusRow) =>
        Math.Clamp(focusRow, 0, _focusRowCount - 1);

    public static int NormalizeBodyScroll(
        int bodyRowCount,
        int focusRow,
        int bodyScrollTop,
        int viewportRows,
        Func<int, int> focusRowToVirtualRow)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        bodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(
            bodyScrollTop,
            bodyRowCount,
            clampedViewportRows);

        int focusVirtualRow = focusRowToVirtualRow(focusRow);
        if (focusVirtualRow >= 0)
        {
            bodyScrollTop = ScrollStateCalculator.EnsureIndexVisible(
                focusVirtualRow,
                bodyScrollTop,
                clampedViewportRows);
        }

        return ScrollStateCalculator.ClampFirstVisibleIndex(
            bodyScrollTop,
            bodyRowCount,
            clampedViewportRows);
    }

    public static int MoveFocus(
        int focusRow,
        int focusRowCount,
        int delta,
        Func<int, bool> isFocusableRow)
    {
        if (focusRowCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(focusRowCount));
        if (delta == 0)
            return Math.Clamp(focusRow, 0, focusRowCount - 1);

        int step = delta < 0 ? -1 : 1;
        int row = Math.Clamp(focusRow, 0, focusRowCount - 1);
        for (int i = 0; i < focusRowCount; i++)
        {
            row = (row + step + focusRowCount) % focusRowCount;
            if (isFocusableRow(row))
                return row;
        }

        return Math.Clamp(focusRow, 0, focusRowCount - 1);
    }

    public static bool TryHitTestBodyRow(
        Rect contentBounds,
        int bodyScrollTop,
        int bodyRowCount,
        int viewportRows,
        int x,
        int y,
        out int virtualRow)
    {
        virtualRow = -1;
        if (x < contentBounds.X || x >= contentBounds.Right ||
            y < contentBounds.Y || y >= contentBounds.Y + Math.Max(1, viewportRows))
        {
            return false;
        }

        virtualRow = bodyScrollTop + y - contentBounds.Y;
        return virtualRow >= 0 && virtualRow < bodyRowCount;
    }

    public static bool TryHandleScrollbarMouse(
        MouseConsoleInputEvent mouse,
        Rect scrollbarBounds,
        int bodyRowCount,
        int viewportRows,
        ref int bodyScrollTop,
        ref ScrollBarDragState? scrollbarDrag)
    {
        int firstVisibleIndex = bodyScrollTop;
        var dragState = scrollbarDrag;
        if (!ScrollBarMouseHandler.TryHandleMouse(
                mouse,
                scrollbarBounds,
                bodyRowCount,
                Math.Max(1, viewportRows),
                ref firstVisibleIndex,
                ref dragState))
        {
            return false;
        }

        bodyScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(
            firstVisibleIndex,
            bodyRowCount,
            Math.Max(1, viewportRows));
        scrollbarDrag = dragState;
        return true;
    }

    private static int FocusRowToVirtualRow(IReadOnlyList<IFormRow> rows, int focusRow)
    {
        int currentFocusRow = 0;
        int virtualRow = 0;
        foreach (var row in rows)
        {
            if (row.IsFocusable)
            {
                if (currentFocusRow == focusRow)
                    return virtualRow;

                currentFocusRow++;
            }

            virtualRow += Math.Max(1, row.Height);
        }

        return -1;
    }

    private static bool IsFocusableRow(IReadOnlyList<IFormRow> rows, int focusRow) =>
        focusRow >= 0 && focusRow < rows.Count(row => row.IsFocusable);

    internal static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
