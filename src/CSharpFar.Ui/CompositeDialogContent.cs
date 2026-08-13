using CSharpFar.Console;
using CSharpFar.Console.Input;
using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

/// <summary>Opaque calculated state for content hosted by <see cref="CompositeDialogHost"/>.</summary>
public interface ICompositeDialogContentFrame { }

public enum CompositeDialogContentEventKind
{
    NotHandled,
    SelectionChanged,
    Confirmed,
}

public readonly record struct CompositeDialogContentInputResult(
    CompositeDialogContentEventKind Kind,
    UiInputResult UiResult,
    bool IsContentRoute)
{
    public static CompositeDialogContentInputResult NotHandled => new(CompositeDialogContentEventKind.NotHandled, UiInputResult.NotHandled, false);
}

/// <summary>
/// Narrow routed-content contract for a composite dialog. Implementations retain ownership
/// of their layout, presentation, interaction targets, and semantic input interpretation.
/// </summary>
public interface ICompositeDialogContent
{
    ICompositeDialogContentFrame CalculateFrame(Rect bounds);
    void Render(IUiCanvas canvas, ICompositeDialogContentFrame frame);
    UiInteractionFragment BuildInteractionFragment(ICompositeDialogContentFrame frame, int focusOrder);
    CompositeDialogContentInputResult RouteInput(ConsoleInputEvent input, ICompositeDialogContentFrame frame, UiInputRouteContext route);
    void ApplyCommittedFrame(ICompositeDialogContentFrame frame);
}
