namespace CSharpFar.App.Menu;

public sealed record MenuHitTestResult
{
    public required MenuHitTestKind Kind { get; init; }
    public int? TopMenuIndex { get; init; }
    public int? DropdownItemIndex { get; init; }
}
