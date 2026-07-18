namespace CSharpFar.Ui;

/// <summary>
/// Provides the built-in colour palettes and resolves a palette by name.
/// </summary>
public static class PaletteRegistry
{
    /// <summary>Default palette: blue file panels with Far-like popup colors.</summary>
    public static ConsolePalette Default { get; } = new()
    {
        Name = "Default",
    };

    /// <summary>FarClassic palette – visual style close to Far Manager.</summary>
    public static ConsolePalette FarClassic { get; } = new()
    {
        Name = "FarClassic",
        Background = ConsoleColor.DarkBlue,
        PanelBackground = ConsoleColor.DarkBlue,
        PanelBorderActiveFg = ConsoleColor.Cyan,
        PanelTitleFocusedFg = ConsoleColor.Cyan,
        PanelPathActiveFg = ConsoleColor.Black,
        PanelPathActiveBg = ConsoleColor.DarkCyan,
        NormalFileFg = ConsoleColor.Cyan,
        DirectoryFg = ConsoleColor.Cyan,
        CursorActiveFg = ConsoleColor.Black,
        CursorActiveBg = ConsoleColor.Green,
        FooterActiveFg = ConsoleColor.Cyan,
        PanelBorderInactiveFg = ConsoleColor.Cyan,
        PanelTitleInactiveFg = ConsoleColor.Cyan,
        NormalFileInactiveFg = ConsoleColor.Cyan,
        ColumnHeaderFg = ConsoleColor.Yellow,
        SelectedFg = ConsoleColor.Black,
        SelectedBg = ConsoleColor.Green,
        CommandLineFg = ConsoleColor.White,
        CommandLineBg = ConsoleColor.Black,
        FunctionKeyBarBg = ConsoleColor.Green,
        DirectoryShortcutBarBg = ConsoleColor.DarkCyan,
    };

    public static IReadOnlyList<ConsolePalette> All { get; } =
    [
        Default,
        FarClassic,
    ];

    public static IReadOnlyList<string> Names { get; } =
        All.Select(p => p.Name).ToArray();

    /// <summary>Resolves a palette by name; falls back to Default for unknown names.</summary>
    public static ConsolePalette Resolve(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Default;
}
