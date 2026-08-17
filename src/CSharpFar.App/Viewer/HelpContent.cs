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
        HelpLineKind.KeyLine => $"  {Key,-18}{Description}",
        HelpLineKind.Plain => Description,
        HelpLineKind.Heading => Description,
        HelpLineKind.Title => Description,
        HelpLineKind.Separator => Description,
        _ => string.Empty,
    };
}

internal sealed record HelpPage(IReadOnlyList<HelpLine> Lines);

/// <summary>Built-in help content shown by the F1 help viewer.</summary>
public static class HelpContent
{
    private static HelpLine H(string heading) => new(HelpLineKind.Heading, Description: heading);
    private static HelpLine K(string key, string desc) => new(HelpLineKind.KeyLine, Key: key, Description: desc);
    private static HelpLine P(string text) => new(HelpLineKind.Plain, Description: text);
    private static HelpLine E() => new(HelpLineKind.Empty);

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
        H("COPY  (F5)"),
        P("  F5 opens the Copy dialog. Press F1 or choose Help there for Copy options."),
        E(),
        H("VIEW MODES"),
        K("Ctrl+O",          "Switch dual-panel workspace / shell output with command line"),
        P("                    Command line remains visible; \u2190 \u2192 edit it in shell output"),
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

    private static readonly HelpLine[] CopyLines =
    [
        new(HelpLineKind.Title, Description: "CSharpFar — Copy"),
        new(HelpLineKind.Separator, Description: new string('═', 60)),
        E(),
        H("COPY DESTINATION"),
        P("  Destination is where the selected files or directories are copied."),
        P("  An ordinary destination is used without name transformation. With several"),
        P("  source items, it is normally a destination directory. The mechanisms below"),
        P("  change destination names or paths when they are explicitly used."),
        E(),
        H("DESTINATION WILDCARDS"),
        P("  With Use template off, * and ? in the final Destination component use FAR"),
        P("  ConvertWildcards transformation. They transform a destination name; they are"),
        P("  not a source filter and not an ordinary glob. Parent directories cannot"),
        P("  contain destination wildcards."),
        P("  Source: report.txt              Destination: *_backup.*"),
        P("  Result: report_backup.txt"),
        P("  Source: analysis_options.yaml   Destination: *_OLD.*"),
        P("  Result: analysis_OLD.yaml"),
        E(),
        H("DESTINATION TEMPLATES"),
        P("  Use template computes a destination path for each source item. Use {name},"),
        P("  {ext}, and {modified:<format>}; use {{ and }} for literal braces."),
        P("  For files, {name} excludes the final extension and {ext} includes its dot."),
        P("  For directories, {name} is the directory name and {ext} is empty. modified"),
        P("  uses a supported .NET DateTime format. Templates may include directories."),
        P("  Template mode cannot be combined with * or ? destination wildcards."),
        P("  Source: analysis_options.yaml"),
        P("  Destination: {name}_OLD{ext}"),
        P("  Result: analysis_options_OLD.yaml"),
        P("  Directory example: archive/{modified:yyyy-MM}/{name}{ext}"),
        E(),
        H("COPY MODE"),
        P("  Normal copies normally and does not retry read or write failures."),
        P("  Reliable is for a complete, correct copy when transient read or destination"),
        P("  write failures occur. It retries failures and safely resumes verified data."),
        P("  Fast salvage is for a failing source: it copies readable files quickly,"),
        P("  records failed files, and continues with later files."),
        E(),
        H("EXISTING FILES"),
        P("  Ask requests a decision. Overwrite replaces the destination. Skip leaves it"),
        P("  unchanged. Rename chooses another destination name. Only newer replaces it"),
        P("  only when the source is newer."),
        E(),
        H("ACCESS RIGHTS AND METADATA"),
        P("  Default uses the normal destination security behavior. Copy attempts to copy"),
        P("  Windows access-control settings. Preserve all timestamps keeps file times;"),
        P("  Preserve attributes keeps ordinary file attributes. Copy contents of symbolic"),
        P("  links follows a link and copies its target contents instead of the link."),
        E(),
        H("FILTER"),
        P("  Use filter enables Filter mask. The mask selects source files that take part"),
        P("  in the copy; it does not change destination names. Example: *.cs"),
        P("  Filter mask       -> selects source files"),
        P("  Destination * ?   -> transforms destination names"),
        P("  Use template      -> computes destination paths and names"),
    ];

    private static readonly HelpPage MainPage = new(Lines);
    private static readonly HelpPage CopyPage = new(CopyLines);

    public static int MaxLineLength { get; } = Lines.Max(l => l.FullText.Length);

    internal static HelpPage GetPage(HelpTopic topic) => topic switch
    {
        HelpTopic.Main => MainPage,
        HelpTopic.Copy => CopyPage,
        _ => throw new ArgumentOutOfRangeException(nameof(topic)),
    };
}
