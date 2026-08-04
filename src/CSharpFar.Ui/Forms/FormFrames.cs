using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

internal enum FormTargetKind
{
    Row,
    BodyScrollbar,
    CompositeChild,
    HistoryDropdown,
    HistoryScrollbar,
    DropdownPopup,
    DropdownScrollbar,
}

internal static class FormTargetIds
{
    public static UiTargetId BodyScrollbar { get; } = new("form.body-scrollbar");

    public static UiTargetId ForExplicitRow(string id) =>
        new($"form.row.id:{Uri.EscapeDataString(id)}");

    public static UiTargetId ForAnonymousRow(long token) =>
        new($"form.row.instance:{token}");

    public static UiTargetId ForCompositeChild(UiTargetId rowTarget, string childId) =>
        new($"{rowTarget.Value}:child:{Uri.EscapeDataString(childId)}");

    public static UiTargetId ForHistoryDropdown(UiTargetId rowTarget) => ForCompositeChild(rowTarget, "popup");
    public static UiTargetId ForHistoryScrollbar(UiTargetId rowTarget) => ForCompositeChild(rowTarget, "scrollbar");
    public static UiTargetId ForDropdownPopup(UiTargetId rowTarget) => ForCompositeChild(rowTarget, "popup");
    public static UiTargetId ForDropdownScrollbar(UiTargetId rowTarget) => ForCompositeChild(rowTarget, "scrollbar");
}

internal sealed record ScrollableFormFrame(
    ConsoleViewport Viewport,
    Rect BodyBounds,
    Rect? FooterBounds,
    int ViewportRows,
    int ScreenHeight,
    int EffectiveScrollTop,
    IReadOnlyList<FormTargetFrame> Targets,
    UiTargetId? DefaultTarget,
    VerticalScrollbarFrame? VerticalScrollbarFrame = null);

internal abstract record FormTargetFrame(UiTargetId Target, FormTargetKind Kind, Rect Bounds, Rect? HitBounds);

internal sealed record FormRowTargetFrame(
    UiTargetId Target,
    FormRow Row,
    int RowIndex,
    int? FocusIndex,
    Rect Bounds,
    Rect? HitBounds,
    FormRowLayout Layout,
    bool IsFooter,
    UiCursorPlacement? Cursor,
    FormCompositeFrame? CompositeFrame)
    : FormTargetFrame(Target, FormTargetKind.Row, Bounds, HitBounds)
{
    public bool IsFocusable => FocusIndex is not null;
    internal DropdownSelectFrame? DropdownFrame => CompositeFrame?.State is DropdownCompositeSnapshot { Frame: var frame } ? frame : null;
}

internal sealed record FormBodyScrollbarTargetFrame : FormTargetFrame
{
    public FormBodyScrollbarTargetFrame(UiTargetId target, Rect bounds, Rect hitBounds)
        : base(target, FormTargetKind.BodyScrollbar, bounds, hitBounds)
    {
    }
}

internal sealed record FormCompositeChildTargetFrame : FormTargetFrame
{
    public FormCompositeChildTargetFrame(UiTargetId target, FormRowTargetFrame owner, FormCompositeFrame compositeFrame, FormCompositeTarget child)
        : base(target, child.Kind, child.Bounds, child.HitBounds ?? child.Bounds)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        CompositeFrame = compositeFrame ?? throw new ArgumentNullException(nameof(compositeFrame));
        Child = child ?? throw new ArgumentNullException(nameof(child));
        if (!CompositeFrame.IsOpen || !CompositeFrame.Overlay!.ChildTargets.Contains(Child))
            throw new ArgumentException("A composite child target must belong to an open composite frame.", nameof(compositeFrame));
    }

    public FormRowTargetFrame Owner { get; }
    public FormCompositeFrame CompositeFrame { get; }
    public FormCompositeTarget Child { get; }
    public UiTargetId ChildTarget => Child.Id;
    public bool CapturesMouse => Child.CapturesMouse;
}

internal readonly record struct FormRouteResult(
    FormInputResult FormResult,
    UiInputResult UiResult);

internal static class FormDialogInput
{
    public static bool ShouldSubmit(
        UiRoutedInput<ScrollableFormFrame> routed,
        FormInputResult result,
        ScrollableFormDialog form)
    {
        if (result.Kind == FormInputResultKind.Submit)
        {
            return true;
        }

        if (result.Kind != FormInputResultKind.NotHandled)
        {
            return false;
        }

        return routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.F10 } ||
            ShouldImplicitlySubmit(routed, result, form);
    }

    public static bool ShouldCancel(FormInputResult result) =>
        result.Kind == FormInputResultKind.Cancel;

    public static bool ShouldImplicitlySubmit(
        UiRoutedInput<ScrollableFormFrame> routed,
        FormInputResult result,
        ScrollableFormDialog form) =>
        result.Kind == FormInputResultKind.NotHandled &&
        routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter } &&
        form.IsFocusedOnSubmitRow;
}

