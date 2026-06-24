namespace CSharpFar.App.Viewer;

/// <summary>Kind of a structured help line.</summary>
public enum HelpLineKind
{
    /// <summary>Top-level title (first line).</summary>
    Title,
    /// <summary>Horizontal separator (═══…).</summary>
    Separator,
    /// <summary>Section heading (e.g. "PANEL NAVIGATION").</summary>
    Heading,
    /// <summary>Key-binding line: <see cref="HelpLine.Key"/> + <see cref="HelpLine.Description"/>.</summary>
    KeyLine,
    /// <summary>Continuation / plain text line.</summary>
    Plain,
    /// <summary>Empty line.</summary>
    Empty,
}

/// <summary>One structured line in the built-in help text.</summary>
/// <param name="Kind">Line kind.</param>
/// <param name="Key">Key combo column (for <see cref="HelpLineKind.KeyLine"/>).</param>
/// <param name="Description">Description / body text.</param>
public sealed record HelpLine(HelpLineKind Kind, string Key = "", string Description = "")
{
    /// <summary>Full rendered text (for scrolling width calculation).</summary>
    public string FullText => Kind switch
    {
        HelpLineKind.KeyLine  => $"  {Key,-18}{Description}",
        HelpLineKind.Plain    => Description,
        HelpLineKind.Heading  => Description,
        HelpLineKind.Title    => Description,
        HelpLineKind.Separator => Description,
        _                      => string.Empty,
    };
}

/// <summary>Built-in help content shown by the F1 help viewer.</summary>
public static class HelpContent
{
    private static HelpLine H(string heading)  => new(HelpLineKind.Heading, Description: heading);
    private static HelpLine K(string key, string desc) => new(HelpLineKind.KeyLine, Key: key, Description: desc);
    private static HelpLine P(string text)     => new(HelpLineKind.Plain, Description: text);
    private static HelpLine E()                => new(HelpLineKind.Empty);

    public static readonly HelpLine[] Lines =
    [
        new(HelpLineKind.Title,     Description: "CSharpFar \u2014 Console Dual-Panel File Manager"),
        new(HelpLineKind.Separator, Description: new string('\u2550', 60)),
        E(),
        H("PANEL NAVIGATION"),
        K("\u2191 \u2193",               "Move cursor"),
        K("\u2190 \u2192",               "Move across columns; edge moves to first / last item"),
        K("PgUp / PgDn",     "Move by page"),
        K("Home",            "First item (or start of command line)"),
        K("End",             "Last item  (or end of command line)"),
        K("Tab",             "Switch active panel"),
        K("Enter",           "Enter directory / execute command"),
        K("Backspace",       "Go to parent directory (or delete in command line)"),
        E(),
        H("FILE OPERATIONS"),
        K("F3",              "View file (text viewer)"),
        K("F4",              "Edit file (text editor)"),
        K("Shift+F4",        "Open / create file in editor"),
        K("F5",              "Copy selected files"),
        K("F6",              "Move / Rename selected files"),
        K("F7",              "Create folder"),
        K("F8",              "Delete selected files (with confirmation)"),
        K("F2",              "User menu  (edit user-menu.json to customise)"),
        E(),
        H("SELECTION"),
        K("Insert",          "Toggle selection on current item"),
        K("Ctrl+A",          "Select all / Deselect all"),
        K("Ctrl+*",          "Invert selection"),
        E(),
        H("SORTING  (active panel)"),
        K("Ctrl+F3",         "Sort by name"),
        K("Ctrl+F4",         "Sort by extension"),
        K("Ctrl+F5",         "Sort by last-write date"),
        K("Ctrl+F6",         "Sort by size"),
        P("  (Press the same key again to reverse sort order)"),
        E(),
        H("HISTORY"),
        K("Alt+F8",          "Command history"),
        K("Alt+F11",         "File history"),
        K("Alt+F12",         "Directory history"),
        E(),
        H("SEARCH"),
        K("Alt+F7",          "Search files by mask and text"),
        E(),
        H("COPY MODES  (F5)"),
        P("  The default copy mode can be changed in the copy dialog."),
        K("Overwrite",        "Replace the destination file unconditionally."),
        K("Skip",             "Leave the destination file untouched."),
        K("Reliable copy",    "Resume-with-tail-validation mode."),
        P("  When a destination file already exists and is shorter than the source,"),
        P("  CSharpFar compares the tail of the destination against the corresponding"),
        P("  bytes of the source. If the tail matches, copying resumes from that point."),
        P("  If a mismatch is found, the overlap is rolled back to the last confirmed"),
        P("  good position before resuming. If the destination cannot be trusted at all"),
        P("  (different size and no valid prefix), the normal conflict dialog appears."),
        E(),
        H("VIEW MODES"),
        K("Ctrl+O",          "Toggle panels on/off \u2014 shows shell output underneath"),
        K("Ctrl+F1",         "Toggle left panel"),
        K("Ctrl+F2",         "Toggle right panel"),
        P("                    Command line remains visible; \u2190 \u2192 edit it while hidden"),
        K("Ctrl+Q",          "Quick view \u2014 preview file in the inactive panel"),
        K("Alt+1",           "Full view mode for active panel"),
        K("Alt+2",           "Brief two-column view mode for active panel"),
        K("F9",              "Top menu: Left, Right, Options"),
        K("Ctrl+S",          "Settings: panel view modes and palette"),
        K("Ctrl+\u2190 / Ctrl+\u2192", "Edit command line cursor while panels are visible"),
        E(),
        H("IN VIEWER  (F3)"),
        K("\u2191 \u2193",               "Scroll lines"),
        K("PgUp / PgDn",     "Scroll by page"),
        K("Alt+PgUp/PgDn",   "Fast page scroll"),
        K("\u2190 \u2192",               "Scroll horizontally"),
        K("Ctrl+\u2190/\u2192",          "Fast horizontal scroll"),
        K("Ctrl+Shift+\u2190/\u2192",    "Start / end of current screen line"),
        K("Home / End",      "Start / end of file"),
        K("F1",              "Help"),
        K("F2 / Shift+F2",   "Toggle wrap / word wrap"),
        K("F3 / F10 / Esc",  "Close viewer"),
        K("F4 / H",          "Switch text / hex mode"),
        K("F6",              "Edit current file"),
        K("F7",              "Find text or hex sequence"),
        K("Shift+F7 / Space","Repeat find forward"),
        K("Alt+F7",          "Repeat find backward"),
        K("F8",              "Cycle UTF-8, CP866, Windows-1251"),
        K("Shift+F8",        "Choose encoding"),
        K("Alt+F8 / G",      "Go to line, percent, or byte offset"),
        K("+ / -",           "Next / previous file from the panel"),
        K("Ctrl+U",          "Clear search highlight"),
        K("Ctrl+C/Ctrl+Ins", "Copy current search match"),
        E(),
        H("IN EDITOR  (F4)"),
        K("F2",              "Save file"),
        K("Ctrl+Home",       "Go to start of file"),
        K("Ctrl+End",        "Go to end of file"),
        K("F10 / Esc",       "Close (prompts to save if there are unsaved changes)"),
        E(),
        H("GENERAL"),
        K("F1",              "This help"),
        K("F10",             "Quit CSharpFar"),
        E(),
        H("CONFIGURATION"),
        P("  Settings:   %APPDATA%\\CSharpFar\\settings.json"),
        P("  User menu:  %APPDATA%\\CSharpFar\\user-menu.json"),
        P("  History:    %APPDATA%\\CSharpFar\\history.json"),
        E(),
        P("  Portable mode: create a file named CSharpFar.portable next to the .exe"),
        P("  All config files will go to CSharpFar.config\\ beside the executable."),
    ];

    public static int MaxLineLength { get; } = Lines.Max(l => l.FullText.Length);
}
