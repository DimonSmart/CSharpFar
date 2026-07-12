using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public readonly record struct UiInputResult(
    bool Handled,
    bool Invalidate,
    UiFocusRequest FocusRequest,
    UiMouseCaptureRequest MouseCaptureRequest)
{
    public static UiInputResult NotHandled { get; } = new(false, false, UiFocusRequest.None, UiMouseCaptureRequest.None);

    public static UiInputResult HandledResult { get; } = new(true, false, UiFocusRequest.None, UiMouseCaptureRequest.None);

    public static UiInputResult HandledAndInvalidate { get; } = new(true, true, UiFocusRequest.None, UiMouseCaptureRequest.None);

    public static UiInputResult InvalidateOnly() =>
        new(false, true, UiFocusRequest.None, UiMouseCaptureRequest.None);

    public static UiInputResult RequestFocus(UiTargetId target, bool invalidate = true) =>
        new(true, invalidate, UiFocusRequest.Set(target), UiMouseCaptureRequest.None);

    public static UiInputResult CaptureMouse(
        UiTargetId target,
        MouseButton button,
        bool invalidate = false) =>
        new(true, invalidate, UiFocusRequest.None, UiMouseCaptureRequest.Capture(target, button));

    public static UiInputResult ReleaseMouse(bool invalidate = false) =>
        new(true, invalidate, UiFocusRequest.None, UiMouseCaptureRequest.Release);
}
