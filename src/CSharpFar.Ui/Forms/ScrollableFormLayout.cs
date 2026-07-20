using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed partial class ScrollableFormDialog
{
    public ScrollableFormFrame Render(FormRenderContext context, UiFocusScope focusScope)
    {
        ArgumentNullException.ThrowIfNull(focusScope);
        if (!ReferenceEquals(_activeFocusScope, focusScope) &&
            _requestedInitialTarget is null &&
            !focusScope.HasFocus &&
            _activeFocusScope?.FocusedTarget is UiTargetId previousTarget)
        {
            _requestedInitialTarget = previousTarget;
        }
        _activeFocusScope = focusScope;
        if (_footerRows.Count > 0 && context.FooterBounds is null)
            throw new InvalidOperationException("Footer bounds are required when footer rows are installed.");
        if (context.FooterBounds is Rect footerBounds && FooterRowCount > footerBounds.Height)
            throw new InvalidOperationException("Footer rows do not fit within the footer bounds.");

        int viewportRows = Math.Max(1, context.BodyBounds.Height);
        int effectiveScrollTop = ClampScrollTop(ScrollTop, viewportRows);
        ScrollableFormFrame provisionalFrame = BuildFrame(context, effectiveScrollTop);
        UiTargetId? effectiveFocusedTarget = focusScope.ResolveFocusedTarget(BuildInteractionFrame(provisionalFrame).Focus);
        if (_ensureFocusedTargetVisibleOnNextRender || focusScope.HasNextCommitRequest)
            effectiveScrollTop = EnsureFocusedTargetVisible(effectiveScrollTop, viewportRows, effectiveFocusedTarget);
        ScrollableFormFrame frame = BuildFrame(context, effectiveScrollTop, effectiveFocusedTarget);
        UiInteractionFrame interactionFrame = BuildInteractionFrame(frame);

        context.Screen.FillRegion(context.BodyBounds, FarDialogStyles.Fill);
        foreach (FormTargetFrame targetFrame in frame.Targets.Where(target => target.Kind == FormTargetKind.Row && !target.IsFooter && IsVisibleInBody(target.Bounds, context.BodyBounds)))
        {
            bool focused = targetFrame.Target == effectiveFocusedTarget;
            targetFrame.Row!.Render(new FormRowRenderContext(context.Screen, targetFrame.Bounds, focused, context.Viewport.Height));
        }

        if (BodyRowCount > viewportRows)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                context.Screen,
                new Rect(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, viewportRows),
                new ScrollState
                {
                    TotalItems = BodyRowCount,
                    ViewportItems = viewportRows,
                    FirstVisibleIndex = effectiveScrollTop,
                },
                new ScrollBarOptions
                {
                    Enabled = true,
                    DrawWhenNotScrollable = false,
                },
                context.ScrollbarStyle);
        }

        if (context.FooterBounds is Rect fixedFooterBounds)
        {
            context.Screen.FillRegion(fixedFooterBounds, FarDialogStyles.Fill);
            foreach (FormTargetFrame targetFrame in frame.Targets.Where(target => target.Kind == FormTargetKind.Row && target.IsFooter))
            {
                bool focused = targetFrame.Target == effectiveFocusedTarget;
                targetFrame.Row!.Render(new FormRowRenderContext(context.Screen, targetFrame.Bounds, focused, context.Viewport.Height));
            }
        }

        RenderFocusedOverlay(context.Screen, frame, effectiveFocusedTarget);

        var snapshot = new FormLayoutSnapshot(
            context.Viewport,
            context.BodyBounds,
            context.FooterBounds,
            viewportRows,
            context.Viewport.Height,
            effectiveScrollTop);
        context.PublishOnStable(() =>
        {
            _stableLayout = snapshot;
            _committedFrame = frame;
            ScrollTop = snapshot.EffectiveScrollTop;
            _ensureFocusedTargetVisibleOnNextRender = false;
            _requestedInitialTarget = null;
            foreach (FormTargetFrame target in frame.Targets.Where(target => target.Kind == FormTargetKind.Row && target.Row is IFormDropdownRow && target.DropdownFrame is not null))
            {
                ((IFormDropdownRow)target.Row!).CommitDropdownFrame(target.DropdownFrame!.Value);
            }
        });
        return frame;
    }

    public UiInteractionFrame BuildInteractionFrame(ScrollableFormFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var focusEntries = frame.Targets
            .Where(target => target is { Kind: FormTargetKind.Row, IsFocusable: true, FocusIndex: not null })
            .OrderBy(target => target.FocusIndex!.Value)
            .Select(target => new UiFocusEntry(target.Target, target.FocusIndex!.Value, IsEnabled: true, target.Cursor))
            .ToArray();
        var hitRegions = frame.Targets
            .Where(target => target.HitBounds is { Width: > 0, Height: > 0 } && (target.Row is null || target.Row.IsEnabled))
            .Select(target => new UiHitRegion(target.Target, target.HitBounds!.Value))
            .ToArray();
        return new UiInteractionFrame(hitRegions, new UiFocusFrame(focusEntries, frame.DefaultTarget));
    }

    private ScrollableFormFrame BuildFrame(
        FormRenderContext context,
        int effectiveScrollTop,
        UiTargetId? overlayTarget = null)
    {
        var targets = new List<FormTargetFrame>();
        int focusIndex = 0;
        int virtualTop = 0;
        for (int rowIndex = 0; rowIndex < _bodyRows.Count; rowIndex++)
        {
            IFormRow row = _bodyRows[rowIndex];
            int rowHeight = Math.Max(1, row.Height);
            bool visible = virtualTop + rowHeight > effectiveScrollTop &&
                virtualTop < effectiveScrollTop + Math.Max(1, context.BodyBounds.Height);
            Rect rowBounds = visible
                ? new Rect(context.BodyBounds.X, context.BodyBounds.Y + virtualTop - effectiveScrollTop, context.BodyBounds.Width, rowHeight)
                : new Rect(context.BodyBounds.X, context.BodyBounds.Y - rowHeight - 1, context.BodyBounds.Width, rowHeight);
            int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
            targets.Add(CreateRowTargetFrame(context.Screen, row, rowIndex, rowFocusIndex, rowBounds, isFooter: false, context.Viewport, context.BodyBounds));
            if (row.IsFocusable)
                focusIndex++;
            virtualTop += rowHeight;
        }

        if (BodyRowCount > Math.Max(1, context.BodyBounds.Height))
        {
            targets.Add(new FormTargetFrame(
                FormTargetIds.BodyScrollbar,
                FormTargetKind.BodyScrollbar,
                Row: null,
                RowIndex: -1,
                FocusIndex: null,
                new Rect(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, Math.Max(1, context.BodyBounds.Height)),
                Intersect(
                    new Rect(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, Math.Max(1, context.BodyBounds.Height)),
                    context.BodyBounds),
                IsFocusable: false,
                IsFooter: false));
        }

        if (context.FooterBounds is Rect footerBounds)
        {
            int footerTop = 0;
            for (int rowIndex = 0; rowIndex < _footerRows.Count; rowIndex++)
            {
                IFormRow row = _footerRows[rowIndex];
                int rowHeight = Math.Max(1, row.Height);
                Rect rowBounds = new(footerBounds.X, footerBounds.Y + footerTop, footerBounds.Width, rowHeight);
                int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
                targets.Add(CreateRowTargetFrame(context.Screen, row, rowIndex, rowFocusIndex, rowBounds, isFooter: true, context.Viewport, footerBounds));
                if (row.IsFocusable)
                    focusIndex++;
                footerTop += rowHeight;
            }
        }

        if (overlayTarget is UiTargetId focusedTarget &&
            targets.FirstOrDefault(target => target.Kind == FormTargetKind.Row && target.Target == focusedTarget) is { Row: { } focusedRow } focusedFrame)
        {
            Rect? activeBounds = focusedFrame.HitBounds;
            if (activeBounds is not null)
            {
                AddRowOverlayTargets(targets, focusedRow, focusedFrame.RowIndex, focusedFrame.Bounds,
                    focusedFrame.IsFooter, focusedFrame.FocusIndex, context.Viewport.Height, focusedTarget);
                AddDropdownOverlayTargets(targets, focusedFrame, focusedTarget);
            }
        }

        UiTargetId? defaultTarget = _requestedInitialTarget;
        if (defaultTarget is null || !targets.Any(target => target.Target == defaultTarget && target.IsFocusable))
            defaultTarget = targets.FirstOrDefault(target => target is { Kind: FormTargetKind.Row, IsFocusable: true })?.Target;

        return new ScrollableFormFrame(
            context.Viewport,
            context.BodyBounds,
            context.FooterBounds,
            Math.Max(1, context.BodyBounds.Height),
            context.Viewport.Height,
            effectiveScrollTop,
            targets,
            defaultTarget);
    }

    private FormTargetFrame CreateRowTargetFrame(
        ScreenRenderer screen,
        IFormRow row,
        int rowIndex,
        int? focusIndex,
        Rect bounds,
        bool isFooter,
        ConsoleViewport viewport,
        Rect activeBounds)
    {
        DropdownSelectFrame? dropdownFrame = row is IFormDropdownRow dropdown
            ? dropdown.BuildDropdownFrame(bounds, viewport)
            : null;
        UiCursorPlacement? cursor = null;
        if (row.IsEnabled && row is IFormCursorProvider cursorProvider &&
            cursorProvider.TryGetCursor(new FormRowRenderContext(screen, bounds, focused: true, viewport.Height), out FormCursorPlacement placement) &&
            placement.X >= bounds.X &&
            placement.X < bounds.Right &&
            placement.Y >= bounds.Y &&
            placement.Y < bounds.Bottom &&
            activeBounds.Contains(placement.X, placement.Y))
        {
            cursor = new UiCursorPlacement(placement.X, placement.Y);
        }

        return new FormTargetFrame(
            RowTarget(row),
            FormTargetKind.Row,
            row,
            rowIndex,
            focusIndex,
            bounds,
            Intersect(bounds, activeBounds),
            row.IsFocusable,
            isFooter,
            cursor,
            dropdownFrame);
    }

    private static bool IsVisibleInBody(Rect bounds, Rect bodyBounds) =>
        bounds.Bottom > bodyBounds.Y && bounds.Y < bodyBounds.Bottom;

    private static Rect? Intersect(Rect first, Rect second)
    {
        int left = Math.Max(first.X, second.X);
        int top = Math.Max(first.Y, second.Y);
        int right = Math.Min(first.Right, second.Right);
        int bottom = Math.Min(first.Bottom, second.Bottom);
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : null;
    }

    private static void AddRowOverlayTargets(
        List<FormTargetFrame> targets,
        IFormRow row,
        int rowIndex,
        Rect rowBounds,
        bool isFooter,
        int? focusIndex,
        int screenHeight,
        UiTargetId rowTarget)
    {
        if (row is not IFormHistoryRow textInput || textInput.History is null)
            return;

        Rect inputBounds = textInput.GetInputBounds(rowBounds);
        SingleLineTextHistoryFrame? historyFrame = SingleLineTextInput.CalculateHistoryDropdownFrame(
            inputBounds.X,
            inputBounds.Y,
            inputBounds.Width,
            screenHeight,
            textInput.History);
        if (historyFrame is not { } frame)
            return;

        targets.Add(new FormTargetFrame(
            FormTargetIds.ForHistoryDropdown(rowTarget),
            FormTargetKind.HistoryDropdown,
            row,
            rowIndex,
            focusIndex,
            frame.PopupBounds,
            frame.PopupBounds,
            IsFocusable: false,
            IsFooter: isFooter));
        if (frame.ScrollbarBounds is Rect scrollbarBounds)
        {
            targets.Add(new FormTargetFrame(
                FormTargetIds.ForHistoryScrollbar(rowTarget),
                FormTargetKind.HistoryScrollbar,
                row,
                rowIndex,
                focusIndex,
                scrollbarBounds,
                scrollbarBounds,
                IsFocusable: false,
                IsFooter: isFooter));
        }
    }

}

