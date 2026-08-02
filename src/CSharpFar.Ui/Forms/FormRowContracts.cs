using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;
using CSharpFar.Core.Models;

namespace CSharpFar.Ui;

public enum FormInputResultKind
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

public readonly record struct FormInputResult(
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

public enum FormRowRole
{
    Normal,
    TextInput,
    Option,
    ButtonBar,
}

public interface IFormRow
{
    string? Id { get; }
    FormRowRole Role { get; }
    bool SubmitOnEnter { get; }
    bool IsEnabled { get; }
    bool IsFocusable { get; }
    int Height { get; }
    void Render(FormRowRenderContext context);
    FormInputResult HandleKey(ConsoleKeyInfo key, FormRowInputContext context);
    FormInputResult HandleMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context);
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
    void ApplyCommittedFrame(FormCompositeFrame frame);
    void RenderOverlay(FormRowRenderContext context, FormCompositeFrame frame);
    FormInputResult RouteKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame);
    FormInputResult RouteMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, UiTargetId? childTarget);
    bool IsAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame);
    void Close();
}

public readonly record struct FormCompositeFrameContext(FormRowLayout Layout, ConsoleViewport Viewport, UiTargetId RowTarget)
{
    public Rect RowBounds => Layout.RowBounds;

    internal FormCompositeFrameContext(Rect rowBounds, ConsoleViewport viewport)
        : this(new FormRowLayout(rowBounds, null, rowBounds), viewport, new UiTargetId("form.row.test"))
    {
    }
}

public interface IFormCompositeSnapshot;
internal interface IFormCompositeCommittedState;

public sealed class FormCompositeFrame
{
    private FormCompositeFrame(IFormCompositeSnapshot? snapshot, IReadOnlyList<FormCompositeTarget> childTargets, IFormCompositeCommittedState? committedState = null)
    {
        Snapshot = snapshot;
        ChildTargets = childTargets;
        CommittedState = committedState;
    }

    public bool IsOpen => Snapshot is not null;
    public IFormCompositeSnapshot? Snapshot { get; }
    public IReadOnlyList<FormCompositeTarget> ChildTargets { get; }
    internal IFormCompositeCommittedState? CommittedState { get; }

    public static FormCompositeFrame Closed() => new(null, []);

    internal static FormCompositeFrame Closed(IFormCompositeCommittedState committedState)
    {
        ArgumentNullException.ThrowIfNull(committedState);
        return new FormCompositeFrame(null, [], committedState);
    }

    public static FormCompositeFrame Open(IFormCompositeSnapshot snapshot, IReadOnlyList<FormCompositeTarget> childTargets)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(childTargets);
        return new FormCompositeFrame(snapshot, Array.AsReadOnly(childTargets.ToArray()));
    }
}

public sealed record FormCompositeTarget(UiTargetId Id, Rect Bounds, Rect? HitBounds = null, FormTargetKind Kind = FormTargetKind.CompositeChild, bool CapturesMouse = false);

public readonly record struct FormCursorPlacement(int X, int Y);

public interface IFormCursorProvider
{
    bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor);
}

