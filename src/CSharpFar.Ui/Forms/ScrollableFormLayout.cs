using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Ui;

namespace CSharpFar.Ui;

public sealed partial class ScrollableFormDialog
{
    public ScrollableFormFrame Render(
        FormRenderContext context,
        IUiFocusState focusScope,
        IReadOnlyList<UiFocusEntry>? surroundingFocusEntries = null,
        UiTargetId? surroundingDefaultFocusTarget = null)
    {
        ArgumentNullException.ThrowIfNull(focusScope);
        if (!ReferenceEquals(_activeFocusState, focusScope) &&
            _requestedInitialTarget is null &&
            !focusScope.HasFocus &&
            _activeFocusState?.FocusedTarget is UiTargetId previousTarget)
        {
            _requestedInitialTarget = previousTarget;
        }
        _activeFocusState = focusScope;
        if (_footerRows.Count > 0 && context.FooterBounds is null)
            throw new InvalidOperationException("Footer bounds are required when footer rows are installed.");
        if (context.FooterBounds is Rect footerBounds && FooterRowCount > footerBounds.Height)
            throw new InvalidOperationException("Footer rows do not fit within the footer bounds.");

        int viewportRows = Math.Max(1, context.BodyBounds.Height);
        int effectiveScrollTop = ClampScrollTop(ScrollTop, viewportRows);
        ScrollableFormFrame provisionalFrame = BuildFrame(context, effectiveScrollTop);
        UiFocusFrame localFocusFrame = BuildInteractionFrame(provisionalFrame).Focus;
        UiFocusFrame candidateFocusFrame = surroundingFocusEntries is null
            ? localFocusFrame
            : new UiFocusFrame(
                surroundingFocusEntries.Concat(localFocusFrame.Entries).ToArray(),
                surroundingDefaultFocusTarget ?? localFocusFrame.DefaultTarget);
        UiTargetId? effectiveFocusedTarget = focusScope.ResolveFocusedTarget(candidateFocusFrame);
        bool focusChanges = effectiveFocusedTarget != focusScope.FocusedTarget;
        if (_ensureFocusedTargetVisibleOnNextRender || focusChanges)
            effectiveScrollTop = EnsureFocusedTargetVisible(effectiveScrollTop, viewportRows, effectiveFocusedTarget);
        ScrollableFormFrame frame = BuildFrame(context, effectiveScrollTop, effectiveFocusedTarget);
        UiInteractionFrame interactionFrame = BuildInteractionFrame(frame);

        context.Canvas.FillRegion(context.BodyBounds, DialogStyles.Fill);
        foreach (FormRowTargetFrame targetFrame in frame.Targets.OfType<FormRowTargetFrame>().Where(target => !target.IsFooter && IsVisibleInBody(target.Bounds, context.BodyBounds)))
        {
            bool focused = targetFrame.Target == effectiveFocusedTarget;
            targetFrame.Row.Render(new FormRowRenderContext(context.Canvas, targetFrame.Bounds, focused, targetFrame.Layout));
        }

        if (BodyRowCount > viewportRows)
        {
            new ScrollBarRenderer().RenderVerticalScrollbar(
                context.Canvas,
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
            context.Canvas.FillRegion(fixedFooterBounds, DialogStyles.Fill);
            foreach (FormRowTargetFrame targetFrame in frame.Targets.OfType<FormRowTargetFrame>().Where(target => target.IsFooter))
            {
                bool focused = targetFrame.Target == effectiveFocusedTarget;
                targetFrame.Row.Render(new FormRowRenderContext(context.Canvas, targetFrame.Bounds, focused, targetFrame.Layout));
            }
        }

        RenderFocusedOverlay(context.Canvas, frame, effectiveFocusedTarget);

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
            _scrollbar.ApplyCommittedFrame(frame.VerticalScrollbarFrame);
            _ensureFocusedTargetVisibleOnNextRender = false;
            _requestedInitialTarget = null;
            foreach (FormRowTargetFrame target in frame.Targets.OfType<FormRowTargetFrame>().Where(target => target.Row is IFormCompositeOwner && target.CompositeFrame is not null))
                if (((IFormCompositeOwner)target.Row).CompositeController is IFormCompositeCommitController commitController)
                    commitController.ApplyCommittedFrame(target.CompositeFrame!);
        });
        return frame;
    }

    public UiInteractionFrame BuildInteractionFrame(ScrollableFormFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return new UiInteractionFrameBuilder()
            .AddFragment(BuildInteractionFragment(frame))
            .SetDefaultFocusTarget(frame.DefaultTarget)
            .Build();
    }

    public UiInteractionFragment BuildInteractionFragment(ScrollableFormFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        UiFocusEntry[] focusEntries = frame.Targets.OfType<FormRowTargetFrame>()
            .Where(target => target.IsFocusable)
            .OrderBy(target => target.FocusIndex!.Value)
            .Select(target => new UiFocusEntry(target.Target, target.FocusIndex!.Value, IsEnabled: true, target.Cursor))
            .ToArray();
        UiHitRegion[] hitRegions = frame.Targets
            .Where(target => target.HitBounds is { Width: > 0, Height: > 0 } && (target is not FormRowTargetFrame row || row.Row.IsEnabled))
            .Select(target => new UiHitRegion(target.Target, target.HitBounds!.Value))
            .ToArray();
        return new UiInteractionFragment(hitRegions, focusEntries);
    }

    private ScrollableFormFrame BuildFrame(
        FormRenderContext context,
        int effectiveScrollTop,
        UiTargetId? overlayTarget = null)
    {
        var targets = new List<FormTargetFrame>();
        bool hasBodyScrollbar = BodyRowCount > Math.Max(1, context.BodyBounds.Height);
        Rect bodyRowBounds = hasBodyScrollbar
            ? new Rect(context.BodyBounds.X, context.BodyBounds.Y, Math.Max(0, context.BodyBounds.Width - 1), context.BodyBounds.Height)
            : context.BodyBounds;
        int resolvedLabelWidth = ResolveLabelWidth(bodyRowBounds.Width);
        int focusIndex = 0;
        int virtualTop = 0;
        for (int rowIndex = 0; rowIndex < _bodyRows.Count; rowIndex++)
        {
            FormRow row = _bodyRows[rowIndex];
            int rowHeight = Math.Max(1, row.Height);
            bool visible = virtualTop + rowHeight > effectiveScrollTop &&
                virtualTop < effectiveScrollTop + Math.Max(1, context.BodyBounds.Height);
            Rect rowBounds = visible
                ? new Rect(bodyRowBounds.X, bodyRowBounds.Y + virtualTop - effectiveScrollTop, bodyRowBounds.Width, rowHeight)
                : new Rect(bodyRowBounds.X, bodyRowBounds.Y - rowHeight - 1, bodyRowBounds.Width, rowHeight);
            int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
            targets.Add(CreateRowTargetFrame(context.Canvas, row, rowIndex, rowFocusIndex, rowBounds, isFooter: false, context.Viewport, bodyRowBounds, resolvedLabelWidth));
            if (row.IsFocusable)
                focusIndex++;
            virtualTop += rowHeight;
        }

        if (BodyRowCount > Math.Max(1, context.BodyBounds.Height))
        {
            Rect bodyScrollbarBounds = new(context.BodyBounds.Right - 1, context.BodyBounds.Y, 1, Math.Max(1, context.BodyBounds.Height));
            if (Intersect(bodyScrollbarBounds, context.BodyBounds) is { } hitBounds)
                targets.Add(new FormBodyScrollbarTargetFrame(FormTargetIds.BodyScrollbar, bodyScrollbarBounds, hitBounds));
        }

        if (context.FooterBounds is Rect footerBounds)
        {
            int footerTop = 0;
            for (int rowIndex = 0; rowIndex < _footerRows.Count; rowIndex++)
            {
                FormRow row = _footerRows[rowIndex];
                int rowHeight = Math.Max(1, row.Height);
                Rect rowBounds = new(footerBounds.X, footerBounds.Y + footerTop, footerBounds.Width, rowHeight);
                int? rowFocusIndex = row.IsFocusable ? focusIndex : null;
                targets.Add(CreateRowTargetFrame(context.Canvas, row, rowIndex, rowFocusIndex, rowBounds, isFooter: true, context.Viewport, footerBounds, resolvedLabelWidth));
                if (row.IsFocusable)
                    focusIndex++;
                footerTop += rowHeight;
            }
        }

        if (overlayTarget is UiTargetId focusedTarget &&
            targets.OfType<FormRowTargetFrame>().FirstOrDefault(target => target.Target == focusedTarget) is { } focusedFrame)
        {
            Rect? activeBounds = focusedFrame.HitBounds;
            if (activeBounds is not null)
            {
                AddCompositeTargets(targets, focusedFrame, focusedTarget);
            }
        }

        UiTargetId? defaultTarget = _requestedInitialTarget;
        if (defaultTarget is null || !targets.OfType<FormRowTargetFrame>().Any(target => target.Target == defaultTarget && target.IsFocusable))
            defaultTarget = targets.OfType<FormRowTargetFrame>().FirstOrDefault(target => target.IsFocusable)?.Target;

        Rect? scrollbarBounds = targets.OfType<FormBodyScrollbarTargetFrame>().FirstOrDefault()?.Bounds;
        var scrollbarFrame = _scrollbar.CalculateFrame(scrollbarBounds, new ScrollState
        {
            TotalItems = BodyRowCount,
            ViewportItems = Math.Max(1, context.BodyBounds.Height),
            FirstVisibleIndex = effectiveScrollTop,
        });
        return new ScrollableFormFrame(
            context.Viewport,
            context.BodyBounds,
            context.FooterBounds,
            Math.Max(1, context.BodyBounds.Height),
            context.Viewport.Height,
            effectiveScrollTop,
            targets,
            defaultTarget,
            scrollbarFrame);
    }

    private FormRowTargetFrame CreateRowTargetFrame(
        IUiCanvas screen,
        FormRow row,
        int rowIndex,
        int? focusIndex,
        Rect bounds,
        bool isFooter,
        ConsoleViewport viewport,
        Rect activeBounds,
        int resolvedLabelWidth)
    {
        FormRowLayout layout = CreateRowLayout(row, bounds, resolvedLabelWidth);
        FormCompositeFrame? compositeFrame = row is IFormCompositeOwner composite
            ? composite.CompositeController.CalculateFrame(new FormCompositeFrameContext(layout, viewport, RowTarget(row)))
            : null;
        UiCursorPlacement? cursor = null;
        if (AllowsCursor(row) && row is IFormCursorProvider cursorProvider &&
            cursorProvider.TryGetCursor(new FormRowRenderContext(screen, bounds, focused: true, layout), out FormCursorPlacement placement) &&
            placement.X >= bounds.X &&
            placement.X < bounds.Right &&
            placement.Y >= bounds.Y &&
            placement.Y < bounds.Bottom &&
            activeBounds.Contains(placement.X, placement.Y))
        {
            cursor = new UiCursorPlacement(placement.X, placement.Y);
        }

        return new FormRowTargetFrame(
            RowTarget(row),
            row,
            rowIndex,
            focusIndex,
            bounds,
            Intersect(bounds, activeBounds),
            layout,
            isFooter,
            cursor,
            compositeFrame);
    }

    private int ResolveLabelWidth(int availableWidth)
    {
        if (LayoutOptions.LabelColumnMode == FormLabelColumnMode.PerRow)
            return 0;

        int desired = LayoutOptions.LabelColumnMode == FormLabelColumnMode.Fixed
            ? LayoutOptions.FixedLabelWidth!.Value
            : _bodyRows.OfType<IFormLabeledRow>().Where(row => row.UseSharedLabelColumn).Select(row => row.DesiredLabelWidth).DefaultIfEmpty().Max();
        return Math.Clamp(desired, 0, Math.Max(0, availableWidth - LayoutOptions.LabelGap - LayoutOptions.MinimumControlWidth));
    }

    private FormRowLayout CreateRowLayout(FormRow row, Rect bounds, int resolvedLabelWidth)
    {
        if (row is not IFormLabeledRow labeled)
            return new FormRowLayout(bounds, null, bounds);

        int desiredLabelWidth = LayoutOptions.LabelColumnMode switch
        {
            FormLabelColumnMode.PerRow => labeled.DesiredLabelWidth,
            _ when labeled.UseSharedLabelColumn => resolvedLabelWidth,
            _ => labeled.DesiredLabelWidth,
        };
        int maximumLabelWidth = Math.Max(0, bounds.Width - LayoutOptions.LabelGap - LayoutOptions.MinimumControlWidth);
        int labelWidth = Math.Min(Math.Max(0, desiredLabelWidth), maximumLabelWidth);
        int gap = labelWidth > 0 ? Math.Min(LayoutOptions.LabelGap, Math.Max(0, bounds.Width - labelWidth)) : 0;
        Rect labelBounds = new(bounds.X, bounds.Y, labelWidth, bounds.Height);
        Rect controlBounds = new(bounds.X + labelWidth + gap, bounds.Y, Math.Max(0, bounds.Width - labelWidth - gap), bounds.Height);
        return new FormRowLayout(bounds, labelBounds, controlBounds);
    }

    private bool AllowsCursor(FormRow row) =>
        row.IsEnabled && LayoutOptions.CursorPolicy switch
        {
            FormCursorPolicy.ControlDefault => true,
            FormCursorPolicy.TextInputsOnly => row.Role == FormRowRole.TextInput,
            FormCursorPolicy.Hidden => false,
            _ => throw new InvalidOperationException("Unknown form cursor policy."),
        };

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

    private static void AddCompositeTargets(
        List<FormTargetFrame> targets,
        FormRowTargetFrame rowFrame,
        UiTargetId rowTarget)
    {
        if (rowFrame.Row is not IFormCompositeOwner || rowFrame.CompositeFrame is not { IsOpen: true } compositeFrame)
            return;

        foreach (FormCompositeTarget child in compositeFrame.Overlay!.ChildTargets)
        {
            targets.Add(new FormCompositeChildTargetFrame(child.Id, rowFrame, compositeFrame, child));
        }
    }

}

