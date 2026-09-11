from pathlib import Path


def replace_once(path: str, old: str, new: str) -> None:
    p = Path(path)
    data = p.read_bytes()
    eol = b"\r\n" if b"\r\n" in data else b"\n"
    old_b = old.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8").replace(b"\n", eol)
    new_b = new.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8").replace(b"\n", eol)
    count = data.count(old_b)
    if count != 1:
        raise SystemExit(f"{path}: expected one match, found {count}")
    p.write_bytes(data.replace(old_b, new_b, 1))


replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """            if (!TryParseRow(lines[separatorIndex - 1].Text, out var header) ||
                !TryParseSeparator(lines[separatorIndex].Text, out var alignments) ||
                header.Count != alignments.Count ||
                header.Count == 0)
            {
                continue;
            }
""",
    """            string headerText = lines[separatorIndex - 1].Text;
            string separatorText = lines[separatorIndex].Text;
            if (!TryParseRow(headerText, out var header) ||
                !TryParseSeparator(separatorText, out var alignments) ||
                header.Count != alignments.Count ||
                header.Count == 0 ||
                (header.Count == 1 && !HasTablePipe(headerText) && !HasTablePipe(separatorText)))
            {
                continue;
            }
""",
)

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """    private static bool TryParseBodyRow(
        ScannedLine line,
        MarkdownTableLayout layout,
        bool updateWidths)
    {
        if (!TryParseRow(line.Text, out var cells) || cells.Count > layout.ColumnCount)
            return false;
""",
    """    private static bool TryParseBodyRow(
        ScannedLine line,
        MarkdownTableLayout layout,
        bool updateWidths)
    {
        if (!HasTablePipe(line.Text) ||
            !TryParseRow(line.Text, out var cells) ||
            cells.Count > layout.ColumnCount)
        {
            return false;
        }
""",
)

replace_once(
    "src/CSharpFar.App/Viewer/ViewerPresentation.cs",
    """    private static List<int> FindPipeSeparators(string source, int start, int end)
    {
""",
    """    private static bool HasTablePipe(string source)
    {
        int start = 0;
        while (start < source.Length && char.IsWhiteSpace(source[start]))
            start++;
        int end = source.Length;
        while (end > start && char.IsWhiteSpace(source[end - 1]))
            end--;
        return FindPipeSeparators(source, start, end).Count > 0;
    }

    private static List<int> FindPipeSeparators(string source, int start, int end)
    {
""",
)

replace_once(
    "docs/viewer-and-editor.md",
    """Text-looking files open as text. Binary-looking files open as a 16-byte-per-row hexadecimal dump. Use `F4` or `H` to switch between text and hex display for the current file.
""",
    """Text-looking files open as text. Binary-looking files open as a 16-byte-per-row hexadecimal dump. Use `F4` or `H` to switch between text and hex display for the current file.

Text presentation starts in `Auto`. Markdown (`.md` and `.markdown`) tables are automatically shown as aligned terminal tables while source navigation, wrapping, and search still operate on the original text. Press `F5` to switch to `Raw` and see the original representation; press `F5` again to return to `Auto`. The selected presentation mode remains active when moving between sibling files with `+` / `-`. Hex output is unchanged.
""",
)

replace_once(
    "docs/viewer-and-editor.md",
    """- `F4` or `H` — switch text/hex mode.
""",
    """- `F4` or `H` — switch text/hex mode.
- `F5` — switch automatic presentation / raw source representation.
""",
)

replace_once(
    "src/CSharpFar.App/Viewer/HelpContent.cs",
    """        K("F4 / H",          "Switch text / hex mode"),
        K("F6",              "Edit current file"),
""",
    """        K("F4 / H",          "Switch text / hex mode"),
        K("F5",              "Switch automatic / raw presentation"),
        K("F6",              "Edit current file"),
""",
)
