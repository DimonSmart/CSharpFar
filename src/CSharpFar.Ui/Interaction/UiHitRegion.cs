using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record UiHitRegion
{
    public UiHitRegion(
        UiTargetId Target,
        Rect Bounds,
        bool Focusable = false,
        int TabOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(Target);

        this.Target = Target;
        this.Bounds = Bounds;
        this.Focusable = Focusable;
        this.TabOrder = TabOrder;
    }

    public UiTargetId Target { get; }

    public Rect Bounds { get; }

    public bool Focusable { get; }

    public int TabOrder { get; }
}
