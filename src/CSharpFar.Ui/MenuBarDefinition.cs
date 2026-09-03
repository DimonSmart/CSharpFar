namespace CSharpFar.Ui;

public sealed record MenuBarDefinition
{
    public required IReadOnlyList<TopMenuItemDefinition> Items { get; init; }
}
