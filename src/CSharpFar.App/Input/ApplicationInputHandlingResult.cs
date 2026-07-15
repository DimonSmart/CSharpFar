namespace CSharpFar.App.Input;

internal readonly record struct ApplicationInputHandlingResult(
    bool Handled,
    bool ShouldRender)
{
    public static ApplicationInputHandlingResult NotHandled { get; } = new(false, false);

    public static ApplicationInputHandlingResult FromHandled(bool shouldRender) => new(true, shouldRender);
}
