namespace CSharpFar.Ui;

/// <summary>
/// Colour palette for all UI elements. Renderers derive CellStyle values from
/// the active palette rather than using hard-coded colours directly.
/// </summary>
public sealed class ConsolePalette
{
    public required string Name { get; init; }

    // Common background (area outside panels)
    public ConsoleColor Background            { get; init; } = ConsoleColor.DarkBlue;

    // Panel background (shared between active and inactive)
    public ConsoleColor PanelBackground       { get; init; } = ConsoleColor.DarkBlue;

    // ── Active panel ──────────────────────────────────────────────────────────
    public ConsoleColor PanelBorderActiveFg   { get; init; } = ConsoleColor.White;
    public ConsoleColor PanelTitleFocusedFg   { get; init; } = ConsoleColor.White;
    public ConsoleColor PanelPathActiveFg     { get; init; } = ConsoleColor.Black;
    public ConsoleColor PanelPathActiveBg     { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor NormalFileFg          { get; init; } = ConsoleColor.White;
    public ConsoleColor DirectoryFg           { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor CursorActiveFg        { get; init; } = ConsoleColor.Black;
    public ConsoleColor CursorActiveBg        { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor FooterActiveFg        { get; init; } = ConsoleColor.DarkCyan;

    // ── Inactive panel ────────────────────────────────────────────────────────
    public ConsoleColor PanelBorderInactiveFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor PanelTitleInactiveFg  { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor NormalFileInactiveFg  { get; init; } = ConsoleColor.Gray;

    // ── Column header (BriefTwoColumns mode) ─────────────────────────────────
    public ConsoleColor ColumnHeaderFg        { get; init; } = ConsoleColor.White;

    // ── Selection (same for active and inactive, like Far Manager) ────────────
    public ConsoleColor SelectedFg            { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor SelectedBg            { get; init; } = ConsoleColor.DarkBlue;

    // ── Command line ──────────────────────────────────────────────────────────
    public ConsoleColor CommandLineFg         { get; init; } = ConsoleColor.White;
    public ConsoleColor CommandLineBg         { get; init; } = ConsoleColor.Black;

    // ── Function key bar ──────────────────────────────────────────────────────
    public ConsoleColor FunctionKeyBarBg      { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor FunctionKeyNumFg      { get; init; } = ConsoleColor.White;
    public ConsoleColor FunctionKeyNumBg      { get; init; } = ConsoleColor.Black;
    public ConsoleColor FunctionKeyTextFg     { get; init; } = ConsoleColor.Black;

    // ── Directory shortcut bar ────────────────────────────────────────────────
    public ConsoleColor DirectoryShortcutBarBg       { get; init; } = ConsoleColor.Blue;
    public ConsoleColor DirectoryShortcutBarNumberFg { get; init; } = ConsoleColor.White;
    public ConsoleColor DirectoryShortcutBarTextFg   { get; init; } = ConsoleColor.White;

    // Horizontal menu bar
    public ConsoleColor MenuBarNormalFg       { get; init; } = ConsoleColor.Black;
    public ConsoleColor MenuBarNormalBg       { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuBarActiveFg       { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuBarActiveBg       { get; init; } = ConsoleColor.Black;

    // Popup menus and menu-like dialogs, including the drive selection list
    public ConsoleColor MenuNormalFg          { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuNormalBg          { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuActiveFg          { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuActiveBg          { get; init; } = ConsoleColor.Black;
    public ConsoleColor MenuHighlightFg       { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor MenuHighlightBg       { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuActiveHighlightFg { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor MenuActiveHighlightBg { get; init; } = ConsoleColor.Black;
    public ConsoleColor MenuDisabledFg        { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor MenuDisabledBg        { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuBorderFg          { get; init; } = ConsoleColor.White;
    public ConsoleColor MenuBorderBg          { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor MenuShadowFg          { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor MenuShadowBg          { get; init; } = ConsoleColor.Black;

    // ── Help viewer ───────────────────────────────────────────────────────────
    public ConsoleColor HelpBodyFg            { get; init; } = ConsoleColor.White;
    public ConsoleColor HelpBodyBg            { get; init; } = ConsoleColor.Black;
    public ConsoleColor HelpHeadingFg         { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor HelpHeadingBg         { get; init; } = ConsoleColor.Black;
    public ConsoleColor HelpKeyFg             { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor HelpKeyBg             { get; init; } = ConsoleColor.Black;
    public ConsoleColor HelpSeparatorFg       { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor HelpSeparatorBg       { get; init; } = ConsoleColor.Black;
}
