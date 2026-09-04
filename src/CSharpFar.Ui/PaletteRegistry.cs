namespace CSharpFar.Ui;

/// <summary>Provides the built-in reusable console UI palettes.</summary>
public static class PaletteRegistry
{
    /// <summary>Default generic console UI palette.</summary>
    public static ConsolePalette Default { get; } = new()
    {
        Name = "Default",
    };

    /// <summary>Built-in palettes available to generic consumers.</summary>
    public static IReadOnlyList<ConsolePalette> All { get; } =
    [
        Default,
    ];
}
