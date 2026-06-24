namespace CSharpFar.Core.Menu;

public sealed record TopMenuItemDefinition
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public char? HotChar { get; init; }
    public required IReadOnlyList<MenuItemDefinition> Children { get; init; }
}
