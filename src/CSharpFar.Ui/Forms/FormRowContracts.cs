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
    UiMouseCaptureRequestKind MouseCapture = UiMouseCaptureRequestKind.None)
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
public interface IFormCompositeRow : IFormRow
{
    bool IsCompositeOpen { get; }
    FormCompositeFrame BuildCompositeFrame(FormCompositeFrameContext context);
    void CommitCompositeFrame(FormCompositeFrame frame);
    void RenderCompositeOverlay(FormRowRenderContext context, FormCompositeFrame frame);
    FormInputResult HandleCompositeKey(ConsoleKeyInfo key, FormRowInputContext context, FormCompositeFrame frame);
    FormInputResult HandleCompositeMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame, string? childTargetId);
    bool IsCompositeAnchorHit(MouseConsoleInputEvent mouse, FormRowMouseContext context, FormCompositeFrame frame);
    void CloseComposite();
}

public readonly record struct FormCompositeFrameContext(Rect RowBounds, ConsoleViewport Viewport);

public sealed record FormCompositeFrame(
    bool IsOpen,
    object? State,
    IReadOnlyList<FormCompositeTarget> ChildTargets);

public sealed record FormCompositeTarget(string Id, Rect Bounds, Rect? HitBounds = null, FormTargetKind Kind = FormTargetKind.CompositeChild, bool CapturesMouse = false);

public readonly record struct FormCursorPlacement(int X, int Y);

public interface IFormCursorProvider
{
    bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor);
}

