using CSharpFar.App.Rendering;

namespace CSharpFar.App.Input;

internal readonly record struct ApplicationInputHandlingResult(
    bool Handled,
    bool ShouldRender,
    ApplicationRenderPart RenderParts)
{
    public static ApplicationInputHandlingResult NotHandled { get; } =
        new(false, false, ApplicationRenderPart.None);

    public static ApplicationInputHandlingResult FromHandled(
        bool shouldRender,
        ApplicationRenderPart renderParts = ApplicationRenderPart.Full) =>
        new(
            true,
            shouldRender,
            shouldRender ? renderParts : ApplicationRenderPart.None);
}
