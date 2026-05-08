namespace CSharpFar.Core.Menu;

public sealed record MenuBarDefinition
{
    public required IReadOnlyList<TopMenuItemDefinition> Items { get; init; }
}
