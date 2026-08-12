namespace CSharpFar.Ui;

/// <summary>Stable visual limits for a semantic selection dialog.</summary>
public sealed record SelectionDialogPresentation(int? MaxWidth, int MaxVisibleRows)
{
    public static SelectionDialogPresentation Standard { get; } = new(MaxWidth: 60, MaxVisibleRows: 15);
}

/// <summary>Semantic options for a standard single-selection dialog.</summary>
public sealed class SelectionDialogOptions<T>
{
    public required string Title { get; init; }

    public required IReadOnlyList<T> Items { get; init; }

    public required Func<T, string> ItemText { get; init; }

    public int SelectedIndex { get; init; }

    public int MaxVisibleRows { get; init; } = 15;

    public int? MaxWidth { get; init; }

    /// <summary>Named presentation preset; takes precedence over individual limits.</summary>
    public SelectionDialogPresentation? Presentation { get; init; }

    public bool DoubleBorder { get; init; }

    public Action<T, int>? SelectionChanged { get; init; }
}
