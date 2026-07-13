using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record UiHitRegion
{
    public UiHitRegion(
        UiTargetId Target,
        Rect Bounds)
    {
        ArgumentNullException.ThrowIfNull(Target);

        this.Target = Target;
        this.Bounds = Bounds;
    }

    public UiTargetId Target { get; }

    public Rect Bounds { get; }

}
