# Viewer and editor

CSharpFar includes a built-in streaming viewer and text editor so common inspection and editing tasks stay inside the file-manager workflow.

## Viewer

Press `F3` to open the selected file in the full-screen viewer.

The viewer uses a streaming path for small and large files. It reads fixed-size byte blocks by offset and keeps a bounded cache, so opening a large log does not require loading the complete file into memory.

Text-looking files open as text. Binary-looking files open as a 16-byte-per-row hexadecimal dump. Use `F4` or `H` to switch between text and hex display for the current file.

### Navigation

- `Home` / `End` — start or end of the file.
- `Up` / `Down` / `PageUp` / `PageDown` — vertical navigation.
- `Alt+PageUp` / `Alt+PageDown` — faster page scrolling.
- `Left` / `Right` — horizontal scrolling.
- `Ctrl+Left` / `Ctrl+Right` — larger horizontal steps.
- `Ctrl+Shift+Left` / `Ctrl+Shift+Right` — start or end of the current screen line.
- `G` or `Alt+F8` — jump to a line number, byte offset or percentage, for example `12000` or `85%`.
- `+` / `-` — next or previous regular file from the panel list.

### Display modes

- `F` — follow a file that keeps growing.
- `F2` — toggle line wrapping.
- `Shift+F2` — switch word/character wrap behavior.
- `F4` or `H` — switch text/hex mode.

### Search

- `F7` — find text; in hex mode the same dialog can search a byte sequence.
- `Shift+F7` or `Space` — repeat search forward.
- `Alt+F7` — search backward.
- `Ctrl+U` — clear the current search highlight.
- `Ctrl+C` or `Ctrl+Insert` — copy the current search match.

### Encodings

- `F8` — cycle common encodings.
- `Shift+F8` — explicitly select automatic detection, UTF-8, UTF-16, Windows ANSI, Windows-1251, Windows-1252 or CP866 for the current viewer session.

Text decoding detects UTF-8 and UTF-16 BOMs, attempts UTF-8 without a BOM and falls back to the current Windows ANSI code page where appropriate. Invalid byte sequences are rendered as replacement characters rather than aborting the viewer. Control characters from file content are rendered inertly instead of being emitted to the terminal.

Quick View (`Ctrl+Q`) remains a bounded preview rather than a full streaming viewer.

Press `F6` from the viewer to open the current local file in the built-in editor. `F3`, `F10` or `Esc` closes the viewer.

## Editor

The built-in editor is designed for practical terminal-based editing without leaving CSharpFar.

It supports Unicode-aware cursor and selection behavior, mouse text selection, scrolling, clipboard interaction and syntax highlighting based on TextMate grammars. Its rendering and input handling use the same terminal UI infrastructure as the rest of CSharpFar, including committed frame state and partial redraw support.

The editor continues to evolve toward the familiar Far-style editing workflow while using modern text-layout and rendering rules internally.
