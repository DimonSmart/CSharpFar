using CSharpFar.App.Rendering;

namespace CSharpFar.App.Input;

internal readonly record struct ApplicationInputHandlingResult(
    bool Handled,
    bool ShouldRender,
    ApplicationRenderPart RenderParts,
    bool ResumesHiddenInteraction)
{
    public static ApplicationInputHandlingResult NotHandled { get; } =
        new(false, false, ApplicationRenderPart.None, false);

    public static ApplicationInputHandlingResult FromHandled(
        bool shouldRender,
        ApplicationRenderPart renderParts = ApplicationRenderPart.Full,
        bool resumesHiddenInteraction = true) =>
        new(
            true,
            shouldRender,
            shouldRender ? renderParts : ApplicationRenderPart.None,
            resumesHiddenInteraction);
}
