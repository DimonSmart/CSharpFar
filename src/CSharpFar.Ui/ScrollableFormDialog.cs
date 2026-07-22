using System.Runtime.CompilerServices;
using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public sealed partial class ScrollableFormDialog
{
    private IReadOnlyList<IFormRow> _bodyRows = [];
    private IReadOnlyList<IFormRow> _footerRows = [];
    private Dictionary<IFormRow, UiTargetId> _targets = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<IFormRow, AnonymousRowTokenBox> _anonymousRowTokens = new();
    private IUiFocusState? _activeFocusState;
    private UiTargetId? _requestedInitialTarget;
    private ScrollableFormFrame? _committedFrame;
    private FormLayoutSnapshot? _stableLayout;
    private bool _ensureFocusedTargetVisibleOnNextRender;
    private long _nextAnonymousRowToken;

    private sealed class AnonymousRowTokenBox
    {
        public AnonymousRowTokenBox(long value) => Value = value;
        public long Value { get; }
    }
    private FormLayoutSnapshot StableLayout => _stableLayout ?? new(default, default, null, 1, 1, ScrollTop);

    public ScrollableFormDialog()
    {
    }

    public ScrollableFormDialog(IReadOnlyList<IFormRow> rows)
    {
        SetRows(rows);
    }

    public int FocusIndex => FocusIndexFromScope(CurrentFocusedTarget) ?? 0;
    public int FocusableCount => TotalFocusableCount;
    public int ScrollTop { get; private set; }
    public ScrollBarDragState? ScrollbarDrag { get; private set; }
    public string? FocusedRowId => FocusedTargetFrame()?.Row?.Id;
    public FormRowRole FocusedRowRole => FocusedTargetFrame()?.Row?.Role ?? FormRowRole.Normal;
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

            IFormRow? first = AllRows().FirstOrDefault(row => row.IsFocusable);
            return first is null ? null : RowTarget(first);
        }
    }

    private int BodyRowCount => _bodyRows.Sum(static row => Math.Max(1, row.Height));
    private int FooterRowCount => _footerRows.Sum(static row => Math.Max(1, row.Height));
    private int BodyFocusableCount => _bodyRows.Count(static row => row.IsFocusable);
    private int FooterFocusableCount => _footerRows.Count(static row => row.IsFocusable);
    private int TotalFocusableCount => BodyFocusableCount + FooterFocusableCount;

    public void SetRows(IReadOnlyList<IFormRow> bodyRows, IReadOnlyList<IFormRow>? footerRows = null)
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

    public UiTargetId GetFocusTarget(string rowId)
    {
        if (string.IsNullOrEmpty(rowId))
            throw new ArgumentException("A form row ID is required.", nameof(rowId));

        IFormRow? row = AllRows().FirstOrDefault(value =>
            value.IsFocusable && string.Equals(value.Id, rowId, StringComparison.Ordinal));
        return row is null
            ? throw new ArgumentException($"No focusable form row has ID '{rowId}'.", nameof(rowId))
            : RowTarget(row);
    }

    public void SetInitialFocus(string rowId)
    {
        if (_committedFrame is not null)
            throw new InvalidOperationException("Initial form focus must be selected before the first committed frame.");

        _requestedInitialTarget = GetFocusTarget(rowId);
        RequestEnsureFocusVisible();
    }

    public int? FindFocusIndexById(string rowId)
    {
        if (string.IsNullOrEmpty(rowId))
            return null;

        int focusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (string.Equals(row.Id, rowId, StringComparison.Ordinal))
                return focusIndex;

            focusIndex++;
        }

        return null;
    }

    private UiTargetId RowTarget(IFormRow row) =>
        _targets.TryGetValue(row, out UiTargetId? target)
            ? target
            : throw new InvalidOperationException("Form row is not installed in this dialog.");

    private Dictionary<IFormRow, UiTargetId> CreateTargetMap(
        IReadOnlyList<IFormRow> bodyRows,
        IReadOnlyList<IFormRow> footerRows)
    {
        var targets = new Dictionary<IFormRow, UiTargetId>(ReferenceEqualityComparer.Instance);
        foreach (IFormRow row in bodyRows.Concat(footerRows))
        {
            targets[row] = string.IsNullOrEmpty(row.Id)
                ? FormTargetIds.ForAnonymousRow(AnonymousRowToken(row))
                : FormTargetIds.ForExplicitRow(row.Id);
        }

        return targets;
    }

    private long AnonymousRowToken(IFormRow row)
    {
        return _anonymousRowTokens.GetValue(row, _ => new AnonymousRowTokenBox(++_nextAnonymousRowToken)).Value;
    }

    private int? FocusIndexFromScope(UiTargetId? target)
    {
        if (target is null)
            return null;

        int focusIndex = 0;
        foreach (IFormRow row in AllRows())
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

    private FormTargetFrame? FocusedTargetFrame()
    {
        UiTargetId? focused = CurrentFocusedTarget;
        if (focused is null)
            return null;

        int focusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (RowTarget(row) == focused)
                return new FormTargetFrame(focused, FormTargetKind.Row, row, -1, focusIndex, default, null, true, false);

            focusIndex++;
        }

        return null;
    }

    private static void RenderFocusedOverlay(IUiCanvas screen, ScrollableFormFrame frame, UiTargetId? focusedTarget)
    {
        if (focusedTarget is null)
            return;

        FormTargetFrame? targetFrame = FindRowTarget(frame, focusedTarget);
        if (targetFrame?.Row is not { } row)
            return;

        bool overlayPublished = frame.Targets.Any(target =>
            ReferenceEquals(target.Row, row) &&
            target.Kind is FormTargetKind.HistoryDropdown or FormTargetKind.DropdownPopup);
        if (!overlayPublished)
            return;

        var context = new FormRowRenderContext(screen, targetFrame.Bounds, focused: true, screenHeight: frame.ScreenHeight);
        if (row is IFormDropdownRow dropdown && targetFrame.DropdownFrame is { } dropdownFrame)
        {
            dropdown.RenderDropdownOverlay(context, dropdownFrame);
            return;
        }

        if (row is IFormOverlayRow overlayRow)
            overlayRow.RenderOverlay(context);
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

    private static bool IsOffscreenBodyTarget(FormTargetFrame target, Rect bodyBounds) =>
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

    private bool TryHandleScrollbarMouse(MouseConsoleInputEvent mouse, Rect scrollbarBounds, int viewportRows)
    {
        int firstVisibleIndex = ScrollTop;
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

        ScrollTop = ScrollStateCalculator.ClampFirstVisibleIndex(firstVisibleIndex, BodyRowCount, Math.Max(1, viewportRows));
        ScrollbarDrag = dragState;
        return true;
    }

    private IFormRow? FocusedRow(int focusIndex)
    {
        int currentFocusIndex = 0;
        foreach (IFormRow row in AllRows())
        {
            if (!row.IsFocusable)
                continue;

            if (currentFocusIndex == focusIndex)
                return row;

            currentFocusIndex++;
        }

        return null;
    }

    private IEnumerable<IFormRow> AllRows()
    {
        foreach (IFormRow row in _bodyRows)
            yield return row;
        foreach (IFormRow row in _footerRows)
            yield return row;
    }

    private static void ValidateUniqueIds(
        IReadOnlyList<IFormRow> bodyRows,
        IReadOnlyList<IFormRow> footerRows)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (IFormRow row in bodyRows.Concat(footerRows))
        {
            if (!string.IsNullOrEmpty(row.Id) && !ids.Add(row.Id))
                throw new InvalidOperationException($"Duplicate form row ID '{row.Id}'.");
        }
    }

    private static void AddDropdownOverlayTargets(
        List<FormTargetFrame> targets,
        FormTargetFrame rowFrame,
        UiTargetId rowTarget)
    {
        if (rowFrame.Row is not IFormDropdownRow ||
            rowFrame.DropdownFrame is not { IsOpen: true } frame)
            return;

        if (frame.PopupBounds is not Rect popupBounds)
            return;

        targets.Add(new FormTargetFrame(
            FormTargetIds.ForDropdownPopup(rowTarget),
            FormTargetKind.DropdownPopup,
            rowFrame.Row,
            rowFrame.RowIndex,
            rowFrame.FocusIndex,
            popupBounds,
            popupBounds,
            IsFocusable: false,
            IsFooter: rowFrame.IsFooter,
            DropdownFrame: frame));

        if (frame.ScrollbarBounds is Rect scrollbarBounds)
        {
            targets.Add(new FormTargetFrame(
                FormTargetIds.ForDropdownScrollbar(rowTarget),
                FormTargetKind.DropdownScrollbar,
                rowFrame.Row,
                rowFrame.RowIndex,
                rowFrame.FocusIndex,
                scrollbarBounds,
                scrollbarBounds,
                IsFocusable: false,
                IsFooter: rowFrame.IsFooter,
                DropdownFrame: frame));
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
        foreach (IFormRow row in _bodyRows)
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
            IFormRow row = _bodyRows[i];
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

        return text.Length <= width ? text.PadRight(width) : text[..width];
    }
}
