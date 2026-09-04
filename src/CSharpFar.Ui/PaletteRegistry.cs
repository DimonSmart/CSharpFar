namespace CSharpFar.Ui;

/// <summary>
/// Provides the built-in colour palettes and resolves a palette by name.
/// </summary>
public static class PaletteRegistry
{
    /// <summary>Default generic console UI palette.</summary>
    public static ConsolePalette Default { get; } = new()
    {
        Name = "Default",
    };

    public static IReadOnlyList<ConsolePalette> All { get; } =
    [
        Default,
    ];

    public static IReadOnlyList<string> Names { get; } =
        All.Select(p => p.Name).ToArray();

    /// <summary>Resolves a palette by name; falls back to Default for unknown names.</summary>
    public static ConsolePalette Resolve(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Default;
}
