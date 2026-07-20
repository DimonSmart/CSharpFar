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

public readonly record struct FormInputResult(FormInputResultKind Kind, string? Command = null)
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

public interface IFormOverlayRow
{
    void RenderOverlay(FormRowRenderContext context);
}

public interface IFormTransientOverlayRow : IFormRow
{
    bool IsOverlayOpen { get; }
    void CancelOverlay();
}

public interface IFormHistoryRow : IFormRow
{
    SingleLineTextHistoryState? History { get; }
    TextInputRowState State { get; }
    Rect GetInputBounds(Rect rowBounds);
    bool IsHistoryArrow(MouseConsoleInputEvent mouse, FormRowMouseContext context);
}

public interface IFormDropdownRow : IFormRow
{
    bool IsDropdownOpen { get; }
    Rect GetFieldBounds(Rect rowBounds);
    DropdownSelectFrame BuildDropdownFrame(Rect rowBounds, ConsoleViewport viewport);
    void RenderDropdownOverlay(FormRowRenderContext context, DropdownSelectFrame frame);
    FormInputResult HandleDropdownKey(ConsoleKeyInfo key, FormRowInputContext context, DropdownSelectFrame frame);
    FormInputResult HandleDropdownMouse(MouseConsoleInputEvent mouse, FormRowMouseContext context, DropdownSelectFrame frame);
    void CommitDropdownFrame(DropdownSelectFrame frame);
    void CloseDropdown();
}

public readonly record struct FormCursorPlacement(int X, int Y);

public interface IFormCursorProvider
{
    bool TryGetCursor(FormRowRenderContext context, out FormCursorPlacement cursor);
}

