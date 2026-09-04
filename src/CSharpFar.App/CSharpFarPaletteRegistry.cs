using CSharpFar.Ui;

namespace CSharpFar.App;

public static class CSharpFarPaletteRegistry
{
    public static CSharpFarPalette Default { get; } = new()
    {
        Name = "Default",
        Ui = PaletteRegistry.Default,
    };

    public static CSharpFarPalette FarClassic { get; } = new()
    {
        Name = "FarClassic",
        Ui = new ConsolePalette
        {
            Name = "FarClassic",
            Foreground = ConsoleColor.Cyan,
            Background = ConsoleColor.DarkBlue,
            SelectedFg = ConsoleColor.Black,
            SelectedBg = ConsoleColor.Green,
            FunctionKeyBarBg = ConsoleColor.Green,
        },
        PanelBorderActiveFg = ConsoleColor.Cyan,
        PanelTitleFocusedFg = ConsoleColor.Cyan,
        NormalFileFg = ConsoleColor.Cyan,
        DirectoryFg = ConsoleColor.Cyan,
        CursorActiveBg = ConsoleColor.Green,
        FooterActiveFg = ConsoleColor.Cyan,
        FileUsageNormalFg = ConsoleColor.Cyan,
        FileUsageSecondaryFg = ConsoleColor.DarkCyan,
        FileUsageReasonTextFg = ConsoleColor.Cyan,
        FileUsageSelectedOwnerBg = ConsoleColor.Green,
        FileUsageActionBarBg = ConsoleColor.Green,
        PanelBorderInactiveFg = ConsoleColor.Cyan,
        PanelTitleInactiveFg = ConsoleColor.Cyan,
        NormalFileInactiveFg = ConsoleColor.Cyan,
        ColumnHeaderFg = ConsoleColor.Yellow,
        DirectoryShortcutBarBg = ConsoleColor.DarkCyan,
    };

    public static IReadOnlyList<CSharpFarPalette> All { get; } = [Default, FarClassic];
    public static IReadOnlyList<string> Names { get; } = All.Select(p => p.Name).ToArray();
    public static CSharpFarPalette Resolve(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
