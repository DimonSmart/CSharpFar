using CSharpFar.Ui;

namespace CSharpFar.App;

public sealed class CSharpFarPalette
{
    public required string Name { get; init; }
    public required ConsolePalette Ui { get; init; }
    public ConsoleColor PanelBackground { get; init; } = ConsoleColor.DarkBlue;
    public ConsoleColor PanelBorderActiveFg { get; init; } = ConsoleColor.White;
    public ConsoleColor PanelTitleFocusedFg { get; init; } = ConsoleColor.White;
    public ConsoleColor PanelPathActiveFg { get; init; } = ConsoleColor.Black;
    public ConsoleColor PanelPathActiveBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor NormalFileFg { get; init; } = ConsoleColor.White;
    public ConsoleColor DirectoryFg { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor CursorActiveFg { get; init; } = ConsoleColor.Black;
    public ConsoleColor CursorActiveBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor FooterActiveFg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor FileUsageNormalFg { get; init; } = ConsoleColor.White;
    public ConsoleColor FileUsageSecondaryFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor FileUsageBlockedFg { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor FileUsageReasonHeadingFg { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor FileUsageReasonTextFg { get; init; } = ConsoleColor.White;
    public ConsoleColor FileUsageSelectedOwnerFg { get; init; } = ConsoleColor.Black;
    public ConsoleColor FileUsageSelectedOwnerBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor FileUsageActionKeyFg { get; init; } = ConsoleColor.White;
    public ConsoleColor FileUsageActionKeyBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor FileUsageActionLabelFg { get; init; } = ConsoleColor.Black;
    public ConsoleColor FileUsageActionBarBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor PanelBorderInactiveFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor PanelTitleInactiveFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor NormalFileInactiveFg { get; init; } = ConsoleColor.Gray;
    public ConsoleColor ColumnHeaderFg { get; init; } = ConsoleColor.White;
    public ConsoleColor CommandLineFg { get; init; } = ConsoleColor.White;
    public ConsoleColor CommandLineBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor DirectoryShortcutBarBg { get; init; } = ConsoleColor.Blue;
    public ConsoleColor DirectoryShortcutBarNumberFg { get; init; } = ConsoleColor.White;
    public ConsoleColor DirectoryShortcutBarNumberBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor DirectoryShortcutBarTextFg { get; init; } = ConsoleColor.White;
}
