namespace CSharpFar.App.Rendering;

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
    public ConsoleColor PanelTitleActiveFg    { get; init; } = ConsoleColor.Black;
    public ConsoleColor PanelTitleActiveBg    { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor NormalFileFg          { get; init; } = ConsoleColor.White;
    public ConsoleColor DirectoryFg           { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor CursorActiveFg        { get; init; } = ConsoleColor.Black;
    public ConsoleColor CursorActiveBg        { get; init; } = ConsoleColor.Cyan;
    public ConsoleColor FooterActiveFg        { get; init; } = ConsoleColor.DarkCyan;

    // ── Inactive panel ────────────────────────────────────────────────────────
    public ConsoleColor PanelBorderInactiveFg { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor PanelTitleInactiveFg  { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor NormalFileInactiveFg  { get; init; } = ConsoleColor.Gray;
    public ConsoleColor DirectoryInactiveFg   { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor CursorInactiveFg      { get; init; } = ConsoleColor.Black;
    public ConsoleColor CursorInactiveBg      { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor FooterInactiveFg      { get; init; } = ConsoleColor.DarkGray;

    // ── Column header (BriefTwoColumns mode) ─────────────────────────────────
    public ConsoleColor ColumnHeaderFg        { get; init; } = ConsoleColor.White;

    // ── Selection (same for active and inactive, like Far Manager) ────────────
    public ConsoleColor SelectedFg            { get; init; } = ConsoleColor.Yellow;
    public ConsoleColor SelectedBg            { get; init; } = ConsoleColor.DarkBlue;

    // ── Command line ──────────────────────────────────────────────────────────
    public ConsoleColor CommandLineFg         { get; init; } = ConsoleColor.White;
    public ConsoleColor CommandLineBg         { get; init; } = ConsoleColor.Black;

    // ── Function key bar ──────────────────────────────────────────────────────
    public ConsoleColor FunctionKeyBarBg      { get; init; } = ConsoleColor.DarkGray;
    public ConsoleColor FunctionKeyNumFg      { get; init; } = ConsoleColor.Black;
    public ConsoleColor FunctionKeyNumBg      { get; init; } = ConsoleColor.DarkCyan;
    public ConsoleColor FunctionKeyTextFg     { get; init; } = ConsoleColor.Black;
    public ConsoleColor FunctionKeyOverflowFg { get; init; } = ConsoleColor.Red;
}
