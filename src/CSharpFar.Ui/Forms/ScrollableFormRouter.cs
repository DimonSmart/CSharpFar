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
        UiInputRouteContext route,
        bool allowUnfocusedButtonHotkeys = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(route);
        _activeFocusState = route.FocusState;
        RestoreCommittedComponentState(frame);

        if (input is KeyConsoleInputEvent { Key: var key })
            return RouteKey(key, frame, route, allowUnfocusedButtonHotkeys);

        if (input is MouseConsoleInputEvent mouse)
            return RouteMouse(mouse, frame, route);

        return new FormRouteResult(FormInputResult.NotHandled, UiInputResult.NotHandled);
    }

    private FormRouteResult RouteKey(
        ConsoleKeyInfo key,
        ScrollableFormFrame frame,
        UiInputRouteContext route,
        bool allowUnfocusedButtonHotkeys)
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
            FormInputResult rowResult = row is IFormCompositeRow composite && targetFrame.CompositeFrame is { } compositeFrame
                ? composite.HandleCompositeKey(key, inputContext, compositeFrame)
                : row.HandleKey(key, inputContext);
            if (rowResult.IsHandled)
            {
                rowResult = WithSourceRowId(rowResult, row.Id);
                return FormResult(rowResult, WithEnsureFocusVisible(FormResultToUi(rowResult, targetFrame.Target), ensureFocusedTargetVisible));
            }
        }

        if (allowUnfocusedButtonHotkeys && key.KeyChar > ' ')
        {
            foreach (FormTargetFrame buttonFrame in frame.Targets.Where(target => target.Row is ButtonRow { IsEnabled: true }))
            {
                FormInputResult buttonResult = buttonFrame.Row!.HandleKey(
                    key,
                    new FormRowInputContext(
                        buttonFrame.FocusIndex ?? -1,
                        focused: false,
                        SingleLineTextInput.AvailableDropdownContentRows(buttonFrame.Bounds.Y, frame.ScreenHeight),
                        buttonFrame.Row.Id,
                        buttonFrame.Row.Role,
                        buttonFrame.Bounds,
                        frame.ScreenHeight));
                if (buttonResult.IsHandled)
                {
                    buttonResult = WithSourceRowId(buttonResult, buttonFrame.Row.Id);
                    return FormResult(buttonResult, FormResultToUi(buttonResult, buttonFrame.Target));
                }
            }
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
        bool closedOverlay = CloseFocusedCompositeOnOutsideClick(mouse, frame, route);
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
            bool handled = TryHandleScrollbarMouse(mouse, frame);
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
                focused: rowFrame.Target == route.FocusState.FocusedTarget || requestFocus,
                frame.ScreenHeight,
                targetFrame.Row.Id,
                targetFrame.Row.Role,
                rowFrame.Layout);
        FormInputResult rowResult = targetFrame.Row is IFormCompositeRow composite &&
            (targetFrame.CompositeFrame ?? rowFrame.CompositeFrame) is { } compositeFrame
            ? composite.HandleCompositeMouse(mouse, mouseContext, compositeFrame, targetFrame.CompositeChildId)
            : targetFrame.Row.HandleMouse(mouse, mouseContext);
        if (rowResult.IsHandled)
            rowResult = WithSourceRowId(rowResult, targetFrame.Row.Id);
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

        if (targetFrame.CapturesMouse &&
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

    private static bool CloseFocusedCompositeOnOutsideClick(
        MouseConsoleInputEvent mouse,
        ScrollableFormFrame frame,
        UiInputRouteContext route)
    {
        if (mouse is not { Kind: MouseEventKind.Down, Button: MouseButton.Left } ||
            route.FocusState.FocusedTarget is not UiTargetId focusedTarget ||
            FindRowTarget(frame, focusedTarget) is not { Row: IFormCompositeRow row, CompositeFrame: { IsOpen: true } compositeFrame } rowFrame)
        {
            return false;
        }

        bool insideChild = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, row) &&
            target.CompositeChildId is not null &&
            target.HitBounds is Rect bounds && bounds.Contains(mouse.X, mouse.Y));
        if (insideChild)
            return false;

        var context = new FormRowMouseContext(rowFrame.Bounds, rowFrame.FocusIndex ?? rowFrame.RowIndex,
            focused: true, frame.ScreenHeight, row.Id, row.Role, rowFrame.Layout);
        if (row.IsCompositeAnchorHit(mouse, context, compositeFrame))
            return false;

        row.CloseComposite();
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

    private static FormInputResult WithSourceRowId(FormInputResult result, string? sourceRowId) =>
        result with { SourceRowId = sourceRowId };

    private UiInputResult FormResultToUi(FormInputResult result, UiTargetId sourceTarget)
    {
        UiMouseCaptureRequest mouseCapture = result.MouseCapture switch
        {
            UiMouseCaptureRequestKind.None => UiMouseCaptureRequest.None,
            UiMouseCaptureRequestKind.Capture => UiMouseCaptureRequest.Capture(sourceTarget, MouseButton.Left),
            UiMouseCaptureRequestKind.Release => UiMouseCaptureRequest.Release,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.MouseCapture, "Unsupported mouse capture request."),
        };
        UiInputResult uiResult = result.Kind switch
        {
            FormInputResultKind.NotHandled => UiInputResult.NotHandled,
            FormInputResultKind.MoveFocusNext => UiInputResultWithFocus(UiFocusRequest.MoveNext),
            FormInputResultKind.MoveFocusPrevious => UiInputResultWithFocus(UiFocusRequest.MovePrevious),
            FormInputResultKind.Handled => UiInputResult.HandledAndInvalidate,
            FormInputResultKind.OverlayChanged => UiInputResult.HandledAndInvalidate,
            _ => UiInputResult.HandledAndInvalidate,
        };

        return mouseCapture.Kind == UiMouseCaptureRequestKind.None
            ? uiResult
            : new UiInputResult(uiResult.Handled, uiResult.Invalidate, uiResult.FocusRequest, mouseCapture);
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
                target.Row is not IFormCompositeRow composite ||
                target.CompositeFrame is not { } compositeFrame)
            {
                continue;
            }

            composite.CommitCompositeFrame(compositeFrame);
        }
    }

    private bool CancelTransientOverlayExcept(UiTargetId? retainedTarget)
    {
        bool canceled = false;
        foreach (IFormRow row in AllRows())
        {
            if (row is not IFormCompositeRow composite)
                continue;

            if (retainedTarget is not null && RowTarget(row) == retainedTarget)
                continue;

            if (composite.IsCompositeOpen)
            {
                composite.CloseComposite();
                canceled = true;
            }
        }

        return canceled;
    }

}

