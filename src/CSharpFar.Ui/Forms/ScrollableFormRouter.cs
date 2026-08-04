using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed partial class ScrollableFormDialog
{
    internal FormRouteResult RouteInput(
        ConsoleInputEvent input,
        ScrollableFormFrame frame,
        UiInputRouteContext route,
        bool allowUnfocusedButtonHotkeys = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(route);
        _activeFocusState = route.FocusState;
        RestoreFocusedCompositeState(frame, route.FocusState.FocusedTarget);
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
            FindRowTarget(frame, target) is { } targetFrame)
        {
            FormRow row = targetFrame.Row;
            ensureFocusedTargetVisible = IsOffscreenBodyTarget(targetFrame, frame.BodyBounds);
            var inputContext = new FormRowInputContext(Focused: true);
            FormInputResult rowResult = row.HandleKey(key, inputContext);
            if (!rowResult.IsHandled && row is IFormCompositeOwner { CompositeController: IFormCompositeKeyboardController keyboardController } &&
                targetFrame.CompositeFrame is { } compositeFrame)
                rowResult = keyboardController.RouteOverlayKey(key, inputContext, compositeFrame);
            if (rowResult.IsHandled)
            {
                rowResult = WithSourceRowId(rowResult, row.Id);
                return FormResult(rowResult, WithEnsureFocusVisible(FormResultToUi(rowResult, targetFrame.Target), ensureFocusedTargetVisible));
            }
        }

        if (allowUnfocusedButtonHotkeys && key.KeyChar > ' ')
        {
            foreach (FormRowTargetFrame buttonFrame in frame.Targets.OfType<FormRowTargetFrame>().Where(target => target.Row is ButtonRow { IsEnabled: true }))
            {
                FormInputResult buttonResult = buttonFrame.Row.HandleKey(
                    key,
                    new FormRowInputContext(Focused: false));
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
            ConsoleKey.Escape when CloseFocusedComposite(frame, route.FocusState.FocusedTarget, commit: false) =>
                FormResult(FormInputResult.OverlayChanged, new UiInputResult(true, true, UiFocusRequest.None, UiMouseCaptureRequest.Release)),
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

        if (targetFrame is FormBodyScrollbarTargetFrame scrollbarTarget)
        {
            bool handled = TryHandleScrollbarMouse(mouse, frame);
            if (!handled && TryHandleWheel(mouse, frame.ViewportRows))
                return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);
            if (!handled)
                return MergeTransientOverlayChange(FormInputResult.NotHandled, UiInputResult.NotHandled, closedOverlay);

            UiMouseCaptureRequest capture = mouse is { Kind: MouseEventKind.Down, Button: MouseButton.Left }
                ? UiMouseCaptureRequest.Capture(scrollbarTarget.Target, MouseButton.Left)
                : UiMouseCaptureRequest.None;
            return MergeTransientOverlayChange(
                FormInputResult.Handled,
                new UiInputResult(true, true, UiFocusRequest.None, capture),
                closedOverlay);
        }

        FormRowTargetFrame rowFrame = targetFrame switch
        {
            FormRowTargetFrame row => row,
            FormCompositeChildTargetFrame child => child.Owner,
            _ => throw new InvalidOperationException("Unsupported form target."),
        };
        bool requestFocus = rowFrame.IsFocusable &&
            route.RouteKind == UiInputRouteKind.HitTarget &&
            mouse is { Button: MouseButton.Left, Kind: MouseEventKind.Down };
        var mouseContext = new FormRowMouseContext(
                Focused: rowFrame.Target == route.FocusState.FocusedTarget || requestFocus,
                rowFrame.Layout);
        FormInputResult rowResult;
        if (targetFrame is FormRowTargetFrame rowTarget)
        {
            rowResult = rowTarget.Row.HandleMouse(mouse, mouseContext);
            if (!rowResult.IsHandled && rowTarget.Row is IFormCompositeOwner owner && rowFrame.CompositeFrame is { } compositeFrame)
                rowResult = owner.CompositeController.RouteMouse(mouse, mouseContext, compositeFrame, null);
        }
        else if (targetFrame is FormCompositeChildTargetFrame childTarget && childTarget.Owner.Row is IFormCompositeOwner owner)
        {
            rowResult = owner.CompositeController.RouteMouse(mouse, mouseContext, childTarget.CompositeFrame, childTarget.ChildTarget);
        }
        else
        {
            rowResult = FormInputResult.NotHandled;
        }
        if (rowResult.IsHandled)
            rowResult = WithSourceRowId(rowResult, rowFrame.Row.Id);
        if (!rowResult.IsHandled && TryHandleWheel(mouse, frame.ViewportRows))
            return MergeTransientOverlayChange(FormInputResult.Handled, UiInputResult.HandledAndInvalidate, closedOverlay);

        UiInputResult uiResult = FormResultToUi(rowResult, targetFrame.Target);
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

        if (targetFrame is FormCompositeChildTargetFrame { CapturesMouse: true } &&
            rowResult.IsHandled &&
            rowResult.MouseCapture == UiMouseCaptureRequestKind.None &&
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
            FindRowTarget(frame, focusedTarget) is not { Row: IFormCompositeOwner row, CompositeFrame: { IsOpen: true } compositeFrame } rowFrame)
        {
            return false;
        }

        bool insideChild = frame.Targets.OfType<FormCompositeChildTargetFrame>().Any(target =>
            ReferenceEquals(target.Owner.Row, row) && target.HitBounds is Rect bounds && bounds.Contains(mouse.X, mouse.Y));
        if (insideChild)
            return false;

        var context = new FormRowMouseContext(Focused: true, rowFrame.Layout);
        if (row.CompositeController.IsAnchorHit(mouse, context, compositeFrame))
            return false;

        row.CompositeController.Close(commit: false);
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
        int current = FocusIndexFromScope(CurrentFocusedTarget) ?? 0;
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
        FormRowTargetFrame? target = frame.Targets.OfType<FormRowTargetFrame>().FirstOrDefault(value =>
            value.IsFocusable &&
            value.FocusIndex == ClampFocusIndex(focusIndex));
        if (target is not null)
            RequestEnsureFocusVisible();
        return target is null
            ? FormResult(FormInputResult.NotHandled, UiInputResult.NotHandled)
            : FormResult(FormInputResult.Handled, UiInputResult.RequestFocus(target.Target));
    }

    private static FormTargetFrame? FindTarget(ScrollableFormFrame frame, UiTargetId target) =>
        frame.Targets.LastOrDefault(value => value.Target == target);

    private static FormRowTargetFrame? FindRowTarget(ScrollableFormFrame frame, UiTargetId target) =>
        frame.Targets.OfType<FormRowTargetFrame>().FirstOrDefault(value => value.Target == target);

    private static FormRowTargetFrame? FindPrimaryRowFrame(ScrollableFormFrame frame, FormRow row) =>
        frame.Targets.OfType<FormRowTargetFrame>().FirstOrDefault(value => ReferenceEquals(value.Row, row));

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

    private bool CancelTransientOverlayExcept(UiTargetId? retainedTarget)
    {
        bool canceled = false;
        foreach (FormRow row in AllRows())
        {
            if (row is not IFormCompositeOwner composite)
                continue;

            if (retainedTarget is not null && RowTarget(row) == retainedTarget)
                continue;

            if (composite.CompositeController.IsOpen)
            {
                composite.CompositeController.Close(commit: false);
                canceled = true;
            }
        }

        return canceled;
    }

    private static void RestoreFocusedCompositeState(ScrollableFormFrame frame, UiTargetId? focusedTarget)
    {
        if (focusedTarget is null || FindRowTarget(frame, focusedTarget) is not { Row: IFormCompositeOwner owner, CompositeFrame: { } compositeFrame })
            return;
        if (owner.CompositeController is IFormCompositeInputFrameController inputController)
            inputController.RestoreInputFrame(compositeFrame);
    }

    internal void CloseTransientOverlays(bool commit)
    {
        foreach (FormRow row in AllRows())
            if (row is IFormCompositeOwner { CompositeController.IsOpen: true } composite)
                composite.CompositeController.Close(commit);
    }

    private static bool CloseFocusedComposite(ScrollableFormFrame frame, UiTargetId? focusedTarget, bool commit)
    {
        if (focusedTarget is null || FindRowTarget(frame, focusedTarget) is not { Row: IFormCompositeOwner owner } || !owner.CompositeController.IsOpen)
            return false;
        owner.CompositeController.Close(commit);
        return true;
    }

}

