namespace CSharpFar.Ui;

/// <summary>
/// Colour palette for reusable UI elements. Renderers derive CellStyle values from
/// the active palette rather than using hard-coded colours directly.
/// </summary>
public sealed class ConsolePalette
{
    public required string Name { get; init; }

    public ConsoleColor Foreground { get; init; } = ConsoleColor.White;
    public ConsoleColor Background { get; init; } = ConsoleColor.DarkBlue;

    // ── Selection (same for active and inactive, like Far Manager) ────────────
    public ConsoleColor SelectedFg { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor SelectedBg { get; init; } = ConsoleColor.DarkBlue;

    // ── Function key bar ──────────────────────────────────────────────────────
    public ConsoleColor FunctionKeyBarBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor FunctionKeyNumFg { get; init; } = ConsoleColor.White;
    public ConsoleColor FunctionKeyNumBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor FunctionKeyTextFg { get; init; } = ConsoleColor.Black;
    public ConsoleColor FunctionKeyGapFg { get; init; } = ConsoleColor.White;
    public ConsoleColor FunctionKeyGapBg { get; init; } = ConsoleColor.Black;

    // Horizontal menu bar
    public ConsoleColor MenuBarNormalFg { get; init; } = ConsoleColor.Black;
    public ConsoleColor MenuBarNormalBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuBarActiveFg { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuBarActiveBg { get; init; } = ConsoleColor.Black;

    // Popup menus and menu-like dialogs, including the drive selection list
    public ConsoleColor MenuNormalFg { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuNormalBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuActiveFg { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuActiveBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor MenuHighlightFg { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor MenuHighlightBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuActiveHighlightFg { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor MenuActiveHighlightBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor MenuDisabledFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor MenuDisabledBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuBorderFg { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuBorderBg { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuShadowFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor MenuShadowBg { get; init; } = ConsoleColor.Black;

    // Dialogs and reusable modal components
    public ConsoleColor DialogForeground { get; init; } = ConsoleColor.Black;
    public ConsoleColor DialogBackground { get; init; } = ConsoleColor.Gray;
    public ConsoleColor DialogBorder { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor DialogTitle { get; init; } = ConsoleColor.Black;
    public ConsoleColor DialogShadowFg { get; init; } = ConsoleColor.Black;
    public ConsoleColor DialogShadowBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor DialogError { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor InputText { get; init; } = ConsoleColor.White;
    public ConsoleColor InputBackground { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor InputFocusedText { get; init; } = ConsoleColor.White;
    public ConsoleColor InputFocusedBackground { get; init; } = ConsoleColor.DarkBlue;
    public ConsoleColor DisabledControlForeground { get; init; } = ConsoleColor.DarkGray;

    // Warning dialogs
    public ConsoleColor WarningForeground { get; init; } = ConsoleColor.White;
    public ConsoleColor WarningBackground { get; init; } = ConsoleColor.DarkRed;
    public ConsoleColor WarningButtonFocusedForeground { get; init; } = ConsoleColor.Black;
    public ConsoleColor WarningButtonFocusedBackground { get; init; } = ConsoleColor.Gray;
}
