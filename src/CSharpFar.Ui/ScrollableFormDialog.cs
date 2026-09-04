using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed partial class ScrollableFormDialog
{
    private IReadOnlyList<FormRow> _bodyRows = [];
    private IReadOnlyList<FormRow> _footerRows = [];
    private Dictionary<FormRow, UiTargetId> _targets = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<object, AnonymousRowTokenBox> _anonymousRowTokens = new();
    private IUiFocusState? _activeFocusState;
    private UiTargetId? _requestedInitialTarget;
    private ScrollableFormFrame? _committedFrame;
    private FormLayoutSnapshot? _stableLayout;
    private bool _ensureFocusedTargetVisibleOnNextRender;
    private long _nextAnonymousRowToken;
    private readonly VerticalScrollbarController _scrollbar = new();

    private sealed class AnonymousRowTokenBox
    {
        public AnonymousRowTokenBox(long value) => Value = value;
        public long Value { get; }
    }
    private FormLayoutSnapshot StableLayout => _stableLayout ?? new(default, default, null, 1, 1, ScrollTop);

    public ScrollableFormDialog(FormLayoutOptions? layoutOptions = null)
    {
        LayoutOptions = (layoutOptions ?? new FormLayoutOptions()).Validate();
    }

    public ScrollableFormDialog(IReadOnlyList<FormRow> rows, FormLayoutOptions? layoutOptions = null)
    {
        LayoutOptions = (layoutOptions ?? new FormLayoutOptions()).Validate();
        SetRows(rows);
    }

    public int ScrollTop { get; private set; }
    internal int FocusIndex => FocusIndexFromScope(CurrentFocusedTarget) ?? 0;
    internal int FocusableCount => TotalFocusableCount;
    public FormLayoutOptions LayoutOptions { get; }
    internal int NaturalContentHeight => NaturalBodyHeight + NaturalFooterHeight;
    internal int NaturalBodyHeight => BodyRowCount;
    internal int NaturalFooterHeight => FooterRowCount;
    internal int NaturalContentWidth
    {
        get
        {
            FormRow[] rows = AllRows().ToArray();
            IFormLabeledRow[] labeled = rows.OfType<IFormLabeledRow>().Where(row => row.UseSharedLabelColumn).ToArray();
            int labeledWidth = labeled.Length == 0 ? 0 :
                labeled.Max(row => row.DesiredLabelWidth) + LayoutOptions.LabelGap + Math.Max(LayoutOptions.MinimumControlWidth, labeled.Max(row => row.DesiredControlWidth));
            return Math.Max(labeledWidth, rows.Select(static row => row.DesiredWidth).DefaultIfEmpty(0).Max());
        }
    }
    public string? FocusedRowId => FocusedTargetFrame()?.Row.Id;
    internal FormRowRole FocusedRowRole => FocusedTargetFrame()?.Row.Role ?? FormRowRole.Normal;
    public bool IsFocusedOnSubmitRow => FocusedTargetFrame()?.Row is { IsFocusable: true, SubmitOnEnter: true };
    private UiTargetId? CurrentFocusedTarget
    {
        get
        {
            if (_requestedInitialTarget is { } requestedInitialTarget)
                return requestedInitialTarget;

            if (_activeFocusState is { } focusState &&
                focusState.ResolveFocusedTarget(BuildLogicalFocusFrame()) is { } resolvedTarget)
            {
                return resolvedTarget;
            }

            FormRow? first = AllRows().FirstOrDefault(row => row.IsFocusable);
            return first is null ? null : RowTarget(first);
        }
    }

    private int BodyRowCount => _bodyRows.Sum(static row => Math.Max(1, row.Height));
    private int FooterRowCount => _footerRows.Sum(static row => Math.Max(1, row.Height));
    private int BodyFocusableCount => _bodyRows.Count(static row => row.IsFocusable);
    private int FooterFocusableCount => _footerRows.Count(static row => row.IsFocusable);
    private int TotalFocusableCount => BodyFocusableCount + FooterFocusableCount;

    public void SetRows(IReadOnlyList<FormRow> bodyRows, IReadOnlyList<FormRow>? footerRows = null)
    {
        footerRows ??= [];
        ValidateUniqueIds(bodyRows, footerRows);
        UiTargetId? focusedTarget = CurrentFocusedTarget;
        _bodyRows = bodyRows;
        _footerRows = footerRows;
        _targets = CreateTargetMap(bodyRows, footerRows);
        if (focusedTarget is not null && !AllRows().Any(row => row.IsFocusable && RowTarget(row) == focusedTarget))
            _requestedInitialTarget = null;
        if (focusedTarget is null || !AllRows().Any(row => row.IsFocusable && RowTarget(row) == focusedTarget))
            _requestedInitialTarget = AllRows().FirstOrDefault(row => row.IsFocusable) is { } first ? RowTarget(first) : null;
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop, BodyRowCount, StableLayout.ViewportRows);
    }

    public bool IsFocused(string rowId) =>
        !string.IsNullOrEmpty(rowId) && string.Equals(FocusedRowId, rowId, StringComparison.Ordinal);

    public bool IsFocused(IFormFocusTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CurrentFocusedTarget == GetFocusTarget(target);
    }

    public UiTargetId GetFocusTarget(string rowId)
    {
        if (string.IsNullOrEmpty(rowId))
            throw new ArgumentException("A form row ID is required.", nameof(rowId));

        FormRow? row = AllRows().FirstOrDefault(value =>
            value.IsFocusable && string.Equals(value.Id, rowId, StringComparison.Ordinal));
        return row is null
            ? throw new ArgumentException($"No focusable form row has ID '{rowId}'.", nameof(rowId))
            : RowTarget(row);
    }

    internal UiTargetId GetFocusTarget(IFormFocusTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        FormRow? row = AllRows().FirstOrDefault(value =>
            value.IsFocusable &&
            (ReferenceEquals(value, target) || value.FocusTarget is { } focusTarget && ReferenceEquals(focusTarget, target)));
        return row is null
            ? throw new ArgumentException("The focus target is not installed in this form.", nameof(target))
            : RowTarget(row);
    }

    public void SetInitialFocus(string rowId)
    {
        if (_committedFrame is not null)
            throw new InvalidOperationException("Initial form focus must be selected before the first committed frame.");

        _requestedInitialTarget = GetFocusTarget(rowId);
        RequestEnsureFocusVisible();
    }

    public void SetInitialFocus(IFormFocusTarget target)
    {
        if (_committedFrame is not null)
            throw new InvalidOperationException("Initial form focus must be selected before the first committed frame.");

        _requestedInitialTarget = GetFocusTarget(target);
        RequestEnsureFocusVisible();
    }


    private UiTargetId RowTarget(FormRow row) =>
        _targets.TryGetValue(row, out UiTargetId? target)
            ? target
            : throw new InvalidOperationException("Form row is not installed in this dialog.");

    private Dictionary<FormRow, UiTargetId> CreateTargetMap(
        IReadOnlyList<FormRow> bodyRows,
        IReadOnlyList<FormRow> footerRows)
    {
        var targets = new Dictionary<FormRow, UiTargetId>(ReferenceEqualityComparer.Instance);
        foreach (FormRow row in bodyRows.Concat(footerRows))
        {
            targets[row] = string.IsNullOrEmpty(row.Id)
                ? FormTargetIds.ForAnonymousRow(AnonymousRowToken(row))
                : FormTargetIds.ForExplicitRow(row.Id);
        }

        return targets;
    }

    private long AnonymousRowToken(FormRow row)
    {
        object owner = row.FocusTarget is { } target
            ? target
            : row is IFormFocusTarget focusTarget
                ? focusTarget
                : row;
        return _anonymousRowTokens.GetValue(owner, _ => new AnonymousRowTokenBox(++_nextAnonymousRowToken)).Value;
    }

    private int? FocusIndexFromScope(UiTargetId? target)
    {
        if (target is null)
            return null;

        int focusIndex = 0;
        foreach (FormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (RowTarget(row) == target)
                return focusIndex;

            focusIndex++;
        }

        return null;
    }

    private UiFocusFrame BuildLogicalFocusFrame()
    {
        var entries = AllRows()
            .Where(row => row.IsFocusable)
            .Select((row, index) => new UiFocusEntry(RowTarget(row), index))
            .ToArray();
        UiTargetId? defaultTarget = _requestedInitialTarget;
        if (defaultTarget is null || !entries.Any(entry => entry.Target == defaultTarget))
            defaultTarget = entries.FirstOrDefault()?.Target;
        return new UiFocusFrame(entries, defaultTarget);
    }

    private FormRowTargetFrame? FocusedTargetFrame()
    {
        UiTargetId? focused = CurrentFocusedTarget;
        if (focused is null)
            return null;

        int focusIndex = 0;
        foreach (FormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (RowTarget(row) == focused)
                return new FormRowTargetFrame(focused, row, -1, focusIndex, default, null, new FormRowLayout(default, null, default), false, null, null);

            focusIndex++;
        }

        return null;
    }

    private static void RenderFocusedOverlay(IUiCanvas screen, ScrollableFormFrame frame, UiTargetId? focusedTarget)
    {
        if (focusedTarget is null)
            return;

        FormRowTargetFrame? targetFrame = FindRowTarget(frame, focusedTarget);
        if (targetFrame is not { } rowFrame)
            return;
        FormRow row = rowFrame.Row;

        bool overlayPublished = frame.Targets.OfType<FormCompositeChildTargetFrame>().Any(target => ReferenceEquals(target.Owner.Row, row));
        if (!overlayPublished || rowFrame.CompositeFrame is not { IsOpen: true } compositeFrame || row is not IFormCompositeOwner composite)
            return;

        var context = new FormRowRenderContext(screen, rowFrame.Bounds, focused: true, rowFrame.Layout);
        composite.CompositeController.RenderOverlay(context, compositeFrame);
    }

    private void RequestEnsureFocusVisible() => _ensureFocusedTargetVisibleOnNextRender = true;

    private void EnsureFocusVisibleNow(int viewportRows)
    {
        ScrollTop = EnsureFocusedTargetVisible(ScrollTop, viewportRows, CurrentFocusedTarget);
    }

    private int ClampScrollTop(int scrollTop, int viewportRows)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        return ScrollStateCalculator.ClampFirstVisibleIndex(scrollTop, BodyRowCount, clampedViewportRows);
    }

    private int EnsureFocusedTargetVisible(int scrollTop, int viewportRows, UiTargetId? focusedTarget)
    {
        int clampedViewportRows = Math.Max(1, viewportRows);
        int effectiveScrollTop = ClampScrollTop(scrollTop, clampedViewportRows);
        int? focusIndex = FocusIndexFromScope(focusedTarget);
        if (focusIndex is null)
            return effectiveScrollTop;

        int focusVirtualRow = FocusIndexToBodyVirtualRow(focusIndex.Value);
        if (focusVirtualRow >= 0)
        {
            effectiveScrollTop = ScrollStateCalculator.EnsureIndexVisible(focusVirtualRow, effectiveScrollTop, clampedViewportRows);
            int focusHeight = Math.Max(1, FocusedRow(focusIndex.Value)?.Height ?? 1);
            if (focusHeight <= clampedViewportRows && focusVirtualRow + focusHeight > effectiveScrollTop + clampedViewportRows)
                effectiveScrollTop = focusVirtualRow + focusHeight - clampedViewportRows;
        }

        return ScrollStateCalculator.ClampFirstVisibleIndex(effectiveScrollTop, BodyRowCount, clampedViewportRows);
    }

    private static bool IsOffscreenBodyTarget(FormRowTargetFrame target, Rect bodyBounds) =>
        !target.IsFooter &&
        (target.HitBounds is null ||
            target.Bounds.Bottom <= bodyBounds.Y ||
            target.Bounds.Y >= bodyBounds.Bottom);

    private bool TryHandleWheel(MouseConsoleInputEvent mouse, int viewportRows, int wheelRows = 3)
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

        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(ScrollTop + delta, BodyRowCount, Math.Max(1, viewportRows));
        return true;
    }

    private bool TryHandleScrollbarMouse(MouseConsoleInputEvent mouse, ScrollableFormFrame frame)
    {
        if (frame.VerticalScrollbarFrame is not { } scrollbarFrame)
        {
            return false;
        }
        VerticalScrollbarInputResult result = _scrollbar.HandleMouse(mouse, scrollbarFrame);
        if (!result.IsHandled)
            return false;
        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(result.FirstVisibleIndex, BodyRowCount, frame.ViewportRows);
        return true;
    }

    private FormRow? FocusedRow(int focusIndex)
    {
        int currentFocusIndex = 0;
        foreach (FormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (currentFocusIndex == focusIndex)
                return row;

            currentFocusIndex++;
        }

        return null;
    }

    private IEnumerable<FormRow> AllRows()
    {
        foreach (FormRow row in _bodyRows)
            yield return row;
        foreach (FormRow row in _footerRows)
            yield return row;
    }

    private static void ValidateUniqueIds(
        IReadOnlyList<FormRow> bodyRows,
        IReadOnlyList<FormRow> footerRows)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormRow row in bodyRows.Concat(footerRows))
        {
            if (!string.IsNullOrEmpty(row.Id) && !ids.Add(row.Id))
                throw new InvalidOperationException($"Duplicate form row ID '{row.Id}'.");
        }
    }

    private int ClampFocusIndex(int focusRow)
    {
        int count = TotalFocusableCount;
        return count <= 0 ? 0 : Math.Clamp(focusRow, 0, count - 1);
    }

    private int FocusIndexToBodyVirtualRow(int focusIndex)
    {
        int currentFocusRow = 0;
        int virtualRow = 0;
        foreach (FormRow row in _bodyRows)
        {
            if (row.IsFocusable)
            {
                if (currentFocusRow == focusIndex)
                    return virtualRow;

                currentFocusRow++;
            }

            virtualRow += Math.Max(1, row.Height);
        }

        return -1;
    }

    private int NearestFocusableIndexAtOrAfterVirtualRow(int virtualRow, int direction)
    {
        int currentFocusIndex = 0;
        int bestBefore = 0;
        for (int i = 0, currentVirtual = 0; i < _bodyRows.Count; i++)
        {
            FormRow row = _bodyRows[i];
            if (row.IsFocusable)
            {
                if (currentVirtual >= virtualRow)
                    return currentFocusIndex;

                bestBefore = currentFocusIndex;
                currentFocusIndex++;
            }

            currentVirtual += Math.Max(1, row.Height);
        }

        return direction > 0 ? Math.Max(0, BodyFocusableCount - 1) : bestBefore;
    }

    private sealed record FormLayoutSnapshot(
        ConsoleViewport Viewport,
        Rect BodyBounds,
        Rect? FooterBounds,
        int ViewportRows,
        int ScreenHeight,
        int EffectiveScrollTop);

    internal static string Fit(string text, int width)
    {
        if (width <= 0)
            return string.Empty;

        return ConsoleTextMetrics.FitToCells(text, width);
    }
}
