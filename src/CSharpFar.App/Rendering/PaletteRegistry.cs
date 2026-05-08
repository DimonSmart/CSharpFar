namespace CSharpFar.App.Rendering;

/// <summary>
/// Provides the built-in colour palettes and resolves a palette by name.
/// </summary>
public static class PaletteRegistry
{
    /// <summary>Default palette – matches the original CSharpFar blue theme.</summary>
    public static ConsolePalette Default { get; } = new()
    {
        Name                  = "Default",
        Background            = ConsoleColor.DarkBlue,
        PanelBackground       = ConsoleColor.DarkBlue,
        PanelBorderActiveFg   = ConsoleColor.White,
        PanelTitleFocusedFg   = ConsoleColor.White,
        PanelTitleActiveFg    = ConsoleColor.Black,
        PanelTitleActiveBg    = ConsoleColor.Cyan,
        PanelPathActiveFg     = ConsoleColor.Black,
        PanelPathActiveBg     = ConsoleColor.DarkCyan,
        NormalFileFg          = ConsoleColor.White,
        DirectoryFg           = ConsoleColor.Cyan,
        CursorActiveFg        = ConsoleColor.Black,
        CursorActiveBg        = ConsoleColor.DarkCyan,
        FooterActiveFg        = ConsoleColor.DarkCyan,
        PanelBorderInactiveFg = ConsoleColor.DarkGray,
        PanelTitleInactiveFg  = ConsoleColor.DarkGray,
        NormalFileInactiveFg  = ConsoleColor.Gray,
        DirectoryInactiveFg   = ConsoleColor.DarkCyan,
        CursorInactiveFg      = ConsoleColor.Black,
        CursorInactiveBg      = ConsoleColor.DarkGray,
        FooterInactiveFg      = ConsoleColor.DarkGray,
        ColumnHeaderFg        = ConsoleColor.White,
        SelectedFg            = ConsoleColor.Yellow,
        SelectedBg            = ConsoleColor.DarkBlue,
        CommandLineFg         = ConsoleColor.White,
        CommandLineBg         = ConsoleColor.Black,
        FunctionKeyBarBg      = ConsoleColor.DarkCyan,
        FunctionKeyNumFg      = ConsoleColor.White,
        FunctionKeyNumBg      = ConsoleColor.Black,
        FunctionKeyTextFg     = ConsoleColor.Black,
        FunctionKeyOverflowFg = ConsoleColor.Red,
        MenuNormalFg          = ConsoleColor.Black,
        MenuNormalBg          = ConsoleColor.DarkBlue,
        MenuActiveFg          = ConsoleColor.White,
        MenuActiveBg          = ConsoleColor.DarkBlue,
        MenuDisabledFg        = ConsoleColor.DarkGray,
        MenuDisabledBg        = ConsoleColor.DarkBlue,
        MenuBorderFg          = ConsoleColor.Black,
        MenuBorderBg          = ConsoleColor.DarkBlue,
        MenuShadowFg          = ConsoleColor.DarkGray,
        MenuShadowBg          = ConsoleColor.Black,
    };

    /// <summary>FarClassic palette – visual style close to Far Manager.</summary>
    public static ConsolePalette FarClassic { get; } = new()
    {
        Name                  = "FarClassic",
        Background            = ConsoleColor.DarkBlue,
        PanelBackground       = ConsoleColor.DarkBlue,
        PanelBorderActiveFg   = ConsoleColor.Cyan,
        PanelTitleFocusedFg   = ConsoleColor.Cyan,
        PanelTitleActiveFg    = ConsoleColor.Cyan,
        PanelTitleActiveBg    = ConsoleColor.DarkBlue,
        PanelPathActiveFg     = ConsoleColor.Black,
        PanelPathActiveBg     = ConsoleColor.DarkCyan,
        NormalFileFg          = ConsoleColor.Cyan,
        DirectoryFg           = ConsoleColor.Cyan,
        CursorActiveFg        = ConsoleColor.Black,
        CursorActiveBg        = ConsoleColor.Green,
        FooterActiveFg        = ConsoleColor.Cyan,
        PanelBorderInactiveFg = ConsoleColor.Cyan,
        PanelTitleInactiveFg  = ConsoleColor.Cyan,
        NormalFileInactiveFg  = ConsoleColor.Cyan,
        DirectoryInactiveFg   = ConsoleColor.Cyan,
        CursorInactiveFg      = ConsoleColor.Black,
        CursorInactiveBg      = ConsoleColor.DarkGray,
        FooterInactiveFg      = ConsoleColor.DarkGray,
        ColumnHeaderFg        = ConsoleColor.Yellow,
        SelectedFg            = ConsoleColor.Black,
        SelectedBg            = ConsoleColor.Green,
        CommandLineFg         = ConsoleColor.White,
        CommandLineBg         = ConsoleColor.Black,
        FunctionKeyBarBg      = ConsoleColor.Green,
        FunctionKeyNumFg      = ConsoleColor.White,
        FunctionKeyNumBg      = ConsoleColor.Black,
        FunctionKeyTextFg     = ConsoleColor.Black,
        FunctionKeyOverflowFg = ConsoleColor.Red,
        MenuNormalFg          = ConsoleColor.Black,
        MenuNormalBg          = ConsoleColor.DarkBlue,
        MenuActiveFg          = ConsoleColor.White,
        MenuActiveBg          = ConsoleColor.DarkBlue,
        MenuDisabledFg        = ConsoleColor.DarkGray,
        MenuDisabledBg        = ConsoleColor.DarkBlue,
        MenuBorderFg          = ConsoleColor.Black,
        MenuBorderBg          = ConsoleColor.DarkBlue,
        MenuShadowFg          = ConsoleColor.DarkGray,
        MenuShadowBg          = ConsoleColor.Black,
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
