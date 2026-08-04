using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

internal enum FormInputResultKind
{
    NotHandled,
    Handled,
    OverlayChanged,
    ValueChanged,
    MoveFocusNext,
    MoveFocusPrevious,
    Submit,
    Cancel,
}

internal readonly record struct FormInputResult(
    FormInputResultKind Kind,
    string? Command = null,
    UiMouseCaptureRequestKind MouseCapture = UiMouseCaptureRequestKind.None,
    string? SourceRowId = null)
{
    public static FormInputResult NotHandled => new(FormInputResultKind.NotHandled);
    public static FormInputResult Handled => new(FormInputResultKind.Handled);
    public static FormInputResult OverlayChanged => new(FormInputResultKind.OverlayChanged);
    public static FormInputResult ValueChanged => new(FormInputResultKind.ValueChanged);
    public static FormInputResult MoveFocusNext => new(FormInputResultKind.MoveFocusNext);
    public static FormInputResult MoveFocusPrevious => new(FormInputResultKind.MoveFocusPrevious);
    public static FormInputResult Submit(string? command = null) => new(FormInputResultKind.Submit, command);
    public static FormInputResult Cancel(string? command = null) => new(FormInputResultKind.Cancel, command);

    public bool IsHandled => Kind != FormInputResultKind.NotHandled;
}

internal enum FormRowRole
{
    Normal,
    TextInput,
    Option,
    ButtonBar,
}

/// <summary>Optional contract for a row that owns interactive child surfaces.</summary>
internal interface IFormCompositeOwner
{
    IFormCompositeController CompositeController { get; }
}

internal interface IFormCompositeController
{
    bool IsOpen { get; }
    FormCompositeFrame CalculateFrame(FormCompositeFrameContext context);
    void RenderOverlay(FormRowRenderContext context, FormCompositeFrame frame);
    FormInputResult RouteMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, UiTargetId? childTarget);
    bool IsAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame);
    void Close(bool commit);
}

internal interface IFormCompositeCommitController
{
    void ApplyCommittedFrame(FormCompositeFrame frame);
}

/// <summary>Temporary stateful-input capability for composites whose input operation needs to restore an open/preview baseline.</summary>
internal interface IFormCompositeInputFrameController
{
    void RestoreInputFrame(FormCompositeFrame frame);
}

internal interface IFormCompositeKeyboardController
{
    FormInputResult RouteOverlayKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame);
}

internal readonly record struct FormCompositeFrameContext(FormRowLayout Layout, ConsoleViewport Viewport, UiTargetId RowTarget)
{
    public Rect RowBounds => Layout.RowBounds;

    internal FormCompositeFrameContext(Rect rowBounds, ConsoleViewport viewport)
        : this(new FormRowLayout(rowBounds, null, rowBounds), viewport, new UiTargetId("form.row.test"))
    {
    }
}

internal interface IFormCompositeSnapshot;
internal sealed class EmptyFormCompositeSnapshot : IFormCompositeSnapshot
{
    public static EmptyFormCompositeSnapshot Instance { get; } = new();
    private EmptyFormCompositeSnapshot() { }
}

internal sealed class FormCompositeOverlayFrame
{
    public FormCompositeOverlayFrame(IReadOnlyList<FormCompositeTarget> childTargets)
    {
        ArgumentNullException.ThrowIfNull(childTargets);
        ChildTargets = Array.AsReadOnly(childTargets.ToArray());
    }

    public IReadOnlyList<FormCompositeTarget> ChildTargets { get; }
}

internal sealed class FormCompositeFrame
{
    private FormCompositeFrame(IFormCompositeSnapshot state, FormCompositeOverlayFrame? overlay)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Overlay = overlay;
    }

    public bool IsOpen => Overlay is not null;
    public IFormCompositeSnapshot State { get; }
    public FormCompositeOverlayFrame? Overlay { get; }

    public static FormCompositeFrame Closed(IFormCompositeSnapshot? state = null) => new(state ?? EmptyFormCompositeSnapshot.Instance, null);

    public static FormCompositeFrame Open(IFormCompositeSnapshot state, IReadOnlyList<FormCompositeTarget> childTargets)
    {
        return new FormCompositeFrame(state, new FormCompositeOverlayFrame(childTargets));
    }
}

internal sealed record FormCompositeTarget(UiTargetId Id, Rect Bounds, Rect? HitBounds = null, FormTargetKind Kind = FormTargetKind.CompositeChild, bool CapturesMouse = false);

internal readonly record struct FormCursorPlacement(int X, int Y);

internal interface IFormCursorProvider
{
    bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor);
}

