using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed partial class ScrollableFormDialog
{
    public FormRouteResult RouteInput(
        ConsoleInputEvent input,
        ScrollableFormFrame frame,
        UiInputRouteContext route)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(route);
        _activeFocusScope = route.FocusScope;
        RestoreCommittedComponentState(frame);

        if (input is KeyConsoleInputEvent { Key: var key })
            return RouteKey(key, frame, route);

        if (input is MouseConsoleInputEvent mouse)
            return RouteMouse(mouse, frame, route);

        return new FormRouteResult(FormInputResult.NotHandled, UiInputResult.NotHandled);
    }

    private FormRouteResult RouteKey(ConsoleKeyInfo key, ScrollableFormFrame frame, UiInputRouteContext route)
    {
        bool ensureFocusedTargetVisible = false;
        if (route.RouteKind == UiInputRouteKind.FocusedTarget &&
            route.Target is UiTargetId target &&
            FindRowTarget(frame, target) is { Row: { } row } targetFrame)
        {
            ensureFocusedTargetVisible = IsOffscreenBodyTarget(targetFrame, frame.BodyBounds);
            int availableDropdownRows = SingleLineTextInput.AvailableDropdownContentRows(
                targetFrame.Bounds.Y,
                frame.ScreenHeight);
            var inputContext = new FormRowInputContext(
                    targetFrame.FocusIndex ?? -1,
                    focused: true,
                    availableDropdownRows,
                    row.Id,
                    row.Role,
                    targetFrame.Bounds,
                    frame.ScreenHeight);
            FormInputResult rowResult;
            if (row is IFormDropdownRow dropdown && targetFrame.DropdownFrame is { } dropdownFrame)
            {
                rowResult = dropdown.HandleDropdownKey(key, inputContext, dropdownFrame);
            }
            else
            {
                rowResult = row.HandleKey(key, inputContext);
            }
            if (rowResult.IsHandled)
                return FormResult(rowResult, WithEnsureFocusVisible(FormResultToUi(rowResult, targetFrame.Target), ensureFocusedTargetVisible));
        }

        return key.Key switch
        {
            ConsoleKey.UpArrow => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MovePrevious)),
            ConsoleKey.DownArrow => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MoveNext)),
            ConsoleKey.PageUp => MoveFocusPage(frame, -1),
            ConsoleKey.PageDown => MoveFocusPage(frame, 1),
            ConsoleKey.Home => SetFocusByIndex(frame, 0),
            ConsoleKey.End => SetFocusByIndex(frame, Math.Max(0, TotalFocusableCount - 1)),
            ConsoleKey.Tab when (key.Modifiers & ConsoleModifiers.Shift) != 0 => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MovePrevious)),
            ConsoleKey.Tab => FormResult(FormInputResult.Handled, UiInputResultWithFocus(UiFocusRequest.MoveNext)),
            ConsoleKey.Escape => FormResult(FormInputResult.Cancel(), UiInputResult.HandledResult),
            _ => FormResult(FormInputResult.NotHandled, UiInputResult.NotHandled),
        };
    }

    private FormRouteResult RouteMouse(MouseConsoleInputEvent mouse, ScrollableFormFrame frame, UiInputRouteContext route)
    {
        bool closedOverlay = CloseFocusedHistoryOnOutsideClick(mouse, frame, route) ||
            CloseFocusedDropdownOnOutsideClick(mouse, frame, route);
        if (route.RouteKind == UiInputRouteKind.Layer)
        {
            if (TryHandleWheel(mouse, frame.ViewportRows))
                return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);
        }

        if (route.Target is not UiTargetId target)
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

        FormTargetFrame? targetFrame = FindTarget(frame, target);
        if (targetFrame is null)
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

        if (targetFrame.Kind == FormTargetKind.BodyScrollbar)
        {
            bool handled = TryHandleScrollbarMouse(mouse, targetFrame.Bounds, frame.ViewportRows);
            if (!handled && TryHandleWheel(mouse, frame.ViewportRows))
                return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);
            if (!handled)
                return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

            UiMouseCaptureRequest capture = mouse is { Kind: MouseEventKind.Down, Button: MouseButton.Left }
                ? UiMouseCaptureRequest.Capture(targetFrame.Target, MouseButton.Left)
                : UiMouseCaptureRequest.None;
            return MergeTransientOverlayChange(
                FormInputResult.Handled,
                new UiInputResult(true, true, UiFocusRequest.None, capture),
                closedOverlay);
        }

        if (targetFrame.Row is null)
            return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

        FormTargetFrame rowFrame = targetFrame.Kind == FormTargetKind.Row
            ? targetFrame
            : FindPrimaryRowFrame(frame, targetFrame.Row) ?? targetFrame;
        bool requestFocus = rowFrame.IsFocusable &&
            route.RouteKind == UiInputRouteKind.HitTarget &&
            mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down };
        var mouseContext = new FormRowMouseContext(
                rowFrame.Bounds,
                rowFrame.FocusIndex ?? rowFrame.RowIndex,
                focused: rowFrame.Target == route.FocusScope.FocusedTarget || requestFocus,
                frame.ScreenHeight,
                targetFrame.Row.Id,
                targetFrame.Row.Role);
        FormInputResult rowResult;
        if (targetFrame.Row is IFormDropdownRow dropdown &&
            (targetFrame.DropdownFrame ?? rowFrame.DropdownFrame) is { } dropdownFrame)
        {
            rowResult = dropdown.HandleDropdownMouse(mouse, mouseContext, dropdownFrame);
        }
        else
        {
            rowResult = targetFrame.Row.HandleMouse(mouse, mouseContext);
        }
        if (!rowResult.IsHandled && TryHandleWheel(mouse, frame.ViewportRows))
            return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);

        UiInputResult uiResult = FormResultToUi(rowResult, rowFrame.Target);
        if (requestFocus)
        {
            RequestEnsureFocusVisible();
            bool canceledOverlay = CancelTransientOverlayExcept(rowFrame.Target);
            uiResult = new UiInputResult(
                true,
                true,
                UiFocusRequest.Set(rowFrame.Target),
                canceledOverlay ? UiMouseCaptureRequest.Release : uiResult.MouseCaptureRequest);
        }

        if (targetFrame.Kind is FormTargetKind.HistoryScrollbar or FormTargetKind.DropdownScrollbar &&
            rowResult.IsHandled &&
            mouse is { Kind: MouseEventKind.Down, Button: MouseButton.Left })
        {
            uiResult = new UiInputResult(
                true,
                true,
                uiResult.FocusRequest,
                UiMouseCaptureRequest.Capture(targetFrame.Target, MouseButton.Left));
        }

        return MergeTransientOverlayChange(rowResult, uiResult, closedOverlay);
    }

    private static bool CloseFocusedHistoryOnOutsideClick(
        MouseConsoleInputEvent mouse,
        ScrollableFormFrame frame,
        UiInputRouteContext route)
    {
        if (mouse is not { Kind: MouseEventKind.Down, Button: MouseButton.Left } ||
            route.FocusScope.FocusedTarget is not UiTargetId focusedTarget ||
            FindRowTarget(frame, focusedTarget)?.Row is not IFormHistoryRow { History: { IsDropdownOpen: true } history } row)
        {
            return false;
        }

        bool insidePopup = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, row) &&
            target.Kind is FormTargetKind.HistoryDropdown or FormTargetKind.HistoryScrollbar &&
            target.HitBounds is Rect bounds && bounds.Contains(mouse.X, mouse.Y));
        if (insidePopup)
            return false;

        bool onHistoryArrow = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, row) &&
            target.Kind == FormTargetKind.Row &&
            target.HitBounds is Rect bounds &&
            bounds.Contains(mouse.X, mouse.Y) &&
            row.IsHistoryArrow(mouse, new FormRowMouseContext(
                target.Bounds,
                target.FocusIndex ?? target.RowIndex,
                focused: true,
                frame.ScreenHeight,
                row.Id,
                row.Role)));
        if (onHistoryArrow)
            return false;

        history.Close();
        row.State.HistoryScrollbarDrag = null;
        return true;
    }

    private static bool CloseFocusedDropdownOnOutsideClick(
        MouseConsoleInputEvent mouse,
        ScrollableFormFrame frame,
        UiInputRouteContext route)
    {
        if (mouse is not { Kind: MouseEventKind.Down, Button: MouseButton.Left } ||
            route.FocusScope.FocusedTarget is not UiTargetId focusedTarget ||
            FindRowTarget(frame, focusedTarget) is not { Row: IFormDropdownRow dropdown, DropdownFrame: { IsOpen: true } dropdownFrame })
        {
            return false;
        }

        bool insideDropdown = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, dropdown) &&
            target.Kind is FormTargetKind.DropdownPopup or FormTargetKind.DropdownScrollbar &&
            target.HitBounds is Rect bounds &&
            bounds.Contains(mouse.X, mouse.Y));
        if (insideDropdown)
            return false;

        bool onField = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, dropdown) &&
            target.Kind == FormTargetKind.Row &&
            target.HitBounds is Rect bounds &&
            bounds.Contains(mouse.X, mouse.Y) &&
            dropdownFrame.FieldBounds.Contains(mouse.X, mouse.Y));
        if (onField)
            return false;

        dropdown.CloseDropdown();
        return true;
    }

    private static FormRouteResult MergeTransientOverlayChange(
        FormInputResult formResult,
        UiInputResult uiResult,
        bool overlayClosed)
    {
        if (!overlayClosed)
            return FormResult(formResult, uiResult);

        FormInputResult mergedFormResult = formResult.Kind == FormInputResultKind.NotHandled
            ? FormInputResult.OverlayChanged
            : formResult;
        return FormResult(
            mergedFormResult,
            new UiInputResult(
                true,
                true,
                uiResult.FocusRequest,
                uiResult.MouseCaptureRequest.Kind == UiMouseCaptureRequestKind.None
                    ? UiMouseCaptureRequest.Release
                    : uiResult.MouseCaptureRequest));
    }

    private static FormRouteResult FormResult(FormInputResult formResult, UiInputResult uiResult) =>
        new(formResult, uiResult);

    private UiInputResult FormResultToUi(FormInputResult result, UiTargetId sourceTarget)
    {
        return result.Kind switch
        {
            FormInputResultKind.NotHandled => UiInputResult.NotHandled,
            FormInputResultKind.MoveFocusNext => UiInputResultWithFocus(UiFocusRequest.MoveNext),
            FormInputResultKind.MoveFocusPrevious => UiInputResultWithFocus(UiFocusRequest.MovePrevious),
            FormInputResultKind.Handled => UiInputResult.HandledAndInvalidate,
            FormInputResultKind.OverlayChanged => UiInputResult.HandledAndInvalidate,
            _ => UiInputResult.HandledAndInvalidate,
        };
    }

    private UiInputResult UiInputResultWithFocus(UiFocusRequest request)
    {
        bool canceledOverlay = CancelTransientOverlayForFocusRequest(request);
        RequestEnsureFocusVisible();
        return new UiInputResult(
            true,
            true,
            request,
            canceledOverlay ? UiMouseCaptureRequest.Release : UiMouseCaptureRequest.None);
    }

    private UiInputResult WithEnsureFocusVisible(UiInputResult result, bool ensure)
    {
        if (ensure)
            RequestEnsureFocusVisible();
        return result;
    }

    private FormRouteResult MoveFocusPage(ScrollableFormFrame frame, int delta)
    {
        int current = FocusIndex;
        if (current >= BodyFocusableCount)
        {
            if (delta < 0 && BodyFocusableCount > 0)
                return SetFocusByIndex(frame, BodyFocusableCount - 1);
            return FormResult(FormInputResult.Handled, UiInputResult.HandledResult);
        }

        int targetVirtual = Math.Clamp(
            FocusIndexToBodyVirtualRow(current) + delta * frame.ViewportRows,
            0,
            Math.Max(0, BodyRowCount - 1));
        return SetFocusByIndex(frame, NearestFocusableIndexAtOrAfterVirtualRow(targetVirtual, delta));
    }

    private FormRouteResult SetFocusByIndex(ScrollableFormFrame frame, int focusIndex)
    {
        FormTargetFrame? target = frame.Targets.FirstOrDefault(value =>
            value is { Kind: FormTargetKind.Row, IsFocusable: true } &&
            value.FocusIndex == ClampFocusIndex(focusIndex));
        if (target is not null)
            RequestEnsureFocusVisible();
        return target is null
            ? FormResult(FormInputResult.NotHandled, UiInputResult.NotHandled)
            : FormResult(FormInputResult.Handled, UiInputResult.RequestFocus(target.Target));
    }

    private static FormTargetFrame? FindTarget(ScrollableFormFrame frame, UiTargetId target) =>
        frame.Targets.LastOrDefault(value => value.Target == target);

    private static FormTargetFrame? FindRowTarget(ScrollableFormFrame frame, UiTargetId target) =>
        frame.Targets.FirstOrDefault(value => value.Target == target && value.Kind == FormTargetKind.Row);

    private static FormTargetFrame? FindPrimaryRowFrame(ScrollableFormFrame frame, IFormRow row) =>
        frame.Targets.FirstOrDefault(value => ReferenceEquals(value.Row, row) && value.Kind == FormTargetKind.Row);

    private bool CancelTransientOverlayForFocusRequest(UiFocusRequest request)
    {
        return request.Kind switch
        {
            UiFocusRequestKind.Set => CancelTransientOverlayExcept(request.Target),
            UiFocusRequestKind.MoveNext or UiFocusRequestKind.MovePrevious or UiFocusRequestKind.Clear =>
                CancelTransientOverlayExcept(null),
            _ => false,
        };
    }

    private static void RestoreCommittedComponentState(ScrollableFormFrame frame)
    {
        foreach (FormTargetFrame target in frame.Targets)
        {
            if (target.Kind != FormTargetKind.Row ||
                target.Row is not IFormDropdownRow dropdown ||
                target.DropdownFrame is not { } dropdownFrame)
            {
                continue;
            }

            dropdown.CommitDropdownFrame(dropdownFrame);
        }
    }

    private bool CancelTransientOverlayExcept(UiTargetId? retainedTarget)
    {
        bool canceled = false;
        foreach (IFormRow row in AllRows())
        {
            if (row is not IFormTransientOverlayRow overlay || !overlay.IsOverlayOpen)
                continue;

            if (retainedTarget is not null && RowTarget(row) == retainedTarget)
                continue;

            overlay.CancelOverlay();
            canceled = true;
        }

        return canceled;
    }

}

