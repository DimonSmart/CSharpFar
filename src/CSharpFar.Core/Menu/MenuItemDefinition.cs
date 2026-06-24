namespace CSharpFar.Core.Menu;

public sealed record MenuItemDefinition
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public char? HotChar { get; init; }
    public MenuItemKind Kind { get; init; } = MenuItemKind.Command;
    public string? CommandId { get; init; }
    public object? CommandArgs { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }
}
