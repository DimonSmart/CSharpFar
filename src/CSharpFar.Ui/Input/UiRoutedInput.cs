using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public sealed record UiRoutedInput<TFrame>(
    ConsoleInputEvent Input,
    TFrame Frame,
    UiTargetId? Target,
    UiInputRouteKind RouteKind);
