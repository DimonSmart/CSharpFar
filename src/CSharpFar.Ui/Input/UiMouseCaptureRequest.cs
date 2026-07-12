using CSharpFar.Console.Input;

namespace CSharpFar.Ui;

public enum UiMouseCaptureRequestKind
{
    None,
    Capture,
    Release,
}

public readonly record struct UiMouseCaptureRequest
{
    public UiMouseCaptureRequest(
        UiMouseCaptureRequestKind kind,
        UiTargetId? target,
        MouseButton? button)
    {
        if (kind == UiMouseCaptureRequestKind.Capture)
        {
            if (target is null)
                throw new ArgumentException("Capture request requires a target.", nameof(target));
            if (button is null)
                throw new ArgumentException("Capture request requires a button.", nameof(button));
            if (!IsCapturable(button.Value))
                throw new ArgumentException("Mouse capture supports only left, right, and middle buttons.", nameof(button));
        }
        else if (target is not null || button is not null)
        {
            throw new ArgumentException("Only Capture request can contain a target or button.");
        }

        Kind = kind;
        Target = target;
        Button = button;
    }

    public UiMouseCaptureRequestKind Kind { get; }

    public UiTargetId? Target { get; }

    public MouseButton? Button { get; }

    public static UiMouseCaptureRequest None { get; } = new(UiMouseCaptureRequestKind.None, null, null);

    public static UiMouseCaptureRequest Release { get; } = new(UiMouseCaptureRequestKind.Release, null, null);

    public static UiMouseCaptureRequest Capture(UiTargetId target, MouseButton button)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new(UiMouseCaptureRequestKind.Capture, target, button);
    }

    internal static bool IsCapturable(MouseButton button) =>
        button is MouseButton.Left or MouseButton.Right or MouseButton.Middle;
}
