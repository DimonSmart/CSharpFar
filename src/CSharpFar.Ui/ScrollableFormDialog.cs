using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed class ScrollableFormDialog
{
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
}
