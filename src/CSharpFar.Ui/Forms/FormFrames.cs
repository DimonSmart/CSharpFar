using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum FormTargetKind
{
    Row,
    BodyScrollbar,
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

    public static UiTargetId ForHistoryDropdown(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:history-dropdown");

    public static UiTargetId ForHistoryScrollbar(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:history-scrollbar");

    public static UiTargetId ForDropdownPopup(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:dropdown-popup");

    public static UiTargetId ForDropdownScrollbar(UiTargetId rowTarget) =>
        new($"{rowTarget.Value}:dropdown-scrollbar");
}

public sealed record ScrollableFormFrame(
    ConsoleViewport Viewport,
    Rect BodyBounds,
    Rect? FooterBounds,
    int ViewportRows,
    int ScreenHeight,
    int EffectiveScrollTop,
    IReadOnlyList<FormTargetFrame> Targets,
    UiTargetId? DefaultTarget,
    VerticalScrollbarFrame? VerticalScrollbarFrame = null);

public sealed record FormTargetFrame(
    UiTargetId Target,
    FormTargetKind Kind,
    IFormRow? Row,
    int RowIndex,
    int? FocusIndex,
    Rect Bounds,
    Rect? HitBounds,
    bool IsFocusable,
    bool IsFooter,
    UiCursorPlacement? Cursor = null,
    DropdownSelectFrame? DropdownFrame = null);

public readonly record struct FormRouteResult(
    FormInputResult FormResult,
    UiInputResult UiResult);

public static class FormDialogInput
{
    public static bool ShouldImplicitlySubmit(
        UiRoutedInput<ScrollableFormFrame> routed,
        FormInputResult result,
        ScrollableFormDialog form) =>
        result.Kind == FormInputResultKind.NotHandled &&
        routed.Input is KeyConsoleInputEvent { Key.Key: ConsoleKey.Enter } &&
        form.IsFocusedOnSubmitRow;
}

