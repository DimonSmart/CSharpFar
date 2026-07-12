using CSharpFar.Console.Models;

namespace CSharpFar.Ui;

public sealed record UiHitRegion(
    UiTargetId Target,
    Rect Bounds,
    bool Focusable = false,
    int TabOrder = 0);
