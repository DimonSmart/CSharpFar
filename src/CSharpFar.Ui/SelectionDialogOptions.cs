namespace CSharpFar.Ui;

/// <summary>Semantic options for a standard single-selection dialog.</summary>
public sealed class SelectionDialogOptions<T>
{
    public required string Title { get; init; }

    public required IReadOnlyList<T> Items { get; init; }

    public required Func<T, string> ItemText { get; init; }

    public int SelectedIndex { get; init; }

    public int MaxVisibleRows { get; init; } = 15;

    public int? MaxWidth { get; init; }

    public bool DoubleBorder { get; init; }

    public Action<T, int>? SelectionChanged { get; init; }
}
