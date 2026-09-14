namespace CSharpFar.Ui;

/// <summary>Generic presentation and lifecycle options for a settings dialog.</summary>
public sealed class SettingsDialogOptions
{
    public SettingsDialogOptions(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    public string Title { get; }
    public string? InitialPageId { get; init; }
    public int PreferredWidth { get; init; } = 80;
    public int PreferredHeight { get; init; } = 24;
    public int MinWidth { get; init; } = 32;
    public int MinHeight { get; init; } = 10;
    public int CompactBreakpoint { get; init; } = 56;
    public bool DoubleBorder { get; init; } = true;
    public DialogResizeMode ResizeMode { get; init; } = DialogResizeMode.Both;
    public int HorizontalMargin { get; init; } = 2;
    public int VerticalMargin { get; init; } = 1;
    public Func<ConsolePalette>? Theme { get; init; }
}

/// <summary>Lifecycle result of a generic settings dialog.</summary>
public enum SettingsDialogResult
{
    Cancelled,
    Saved,
}
