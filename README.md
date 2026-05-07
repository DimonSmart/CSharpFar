# CSharpFar

A console dual-panel file manager for Windows, inspired by Far Manager.

Built with C# and .NET 10.

## Current status

**Stage 19 + Spec 003 additions complete** — panel view modes, palettes, Far-like polish, and settings dialog are implemented. 226 tests passing.

## Requirements

- .NET 10 SDK
- Windows (primary target platform; Win32 P/Invoke used for console buffer Capture/Restore)

## Build & run

```bash
dotnet build
dotnet run --project src/CSharpFar.App
```

## Test

```bash
dotnet test
```

## Publish (single-file executable)

```bash
dotnet publish src/CSharpFar.App/CSharpFar.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true
```

The output binary is placed in `src/CSharpFar.App/bin/Release/net10.0/win-x64/publish/`.

## Portable mode

Create a file named `CSharpFar.portable` next to the executable.
All configuration files (`settings.json`, `user-menu.json`, `history.json`) will be stored in `CSharpFar.config\` beside the executable instead of `%APPDATA%\CSharpFar\`.

## Keyboard reference

| Key | Action |
|-----|--------|
| `↑ ↓` | Move cursor in panel |
| `PgUp / PgDn` | Move by page |
| `Home / End` | First / last item |
| `Tab` | Switch active panel |
| `Enter` | Enter directory / execute command |
| `Backspace` | Parent directory / delete in command line |
| `Insert` | Toggle file selection |
| `Ctrl+A` | Select all / deselect all |
| `Ctrl+*` | Invert selection |
| `Ctrl+F3/F4/F5/F6` | Sort by name / extension / date / size |
| **F1** | Help |
| **F2** | User menu |
| **F3** | View file |
| **F4** | Edit file |
| **F5** | Copy |
| **F6** | Move / Rename |
| **F7** | Create folder |
| **F8** | Delete |
| **F10** | Quit |
| `Alt+F7` | Search files by mask |
| `Alt+F8` | Command history |
| `Alt+F11` | File history |
| `Alt+F12` | Directory history |
| `Ctrl+O` | Toggle panels / show shell output |
| `Ctrl+Q` | Quick view (file preview in inactive panel) |
| `Alt+1` | Full view mode for active panel |
| `Alt+2` | Brief two-column view mode for active panel |
| `Ctrl+S` / `F9` | Settings: panel view modes and palette |

## Solution structure

```
CSharpFar.sln
/src
  /CSharpFar.App        — entry point, composition root
  /CSharpFar.Core       — domain models and service abstractions
  /CSharpFar.Console    — console abstraction layer (IConsoleDriver, renderer)
  /CSharpFar.FileSystem — file system operations
  /CSharpFar.Shell      — shell execution service
/tests
  /CSharpFar.Tests      — xUnit test project (226 tests)
```

## Configuration

`settings.json` is created automatically on first run. Key options:

```json
{
  "ui": {
    "showHiddenFiles": true,
    "showSystemFiles": true,
    "confirmDelete": true,
    "palette": "Default"
  },
  "shell": {
    "executable": "cmd.exe",
    "argumentsFormat": "/c {0}"
  },
  "panels": {
    "leftStartDirectory": null,
    "rightStartDirectory": null,
    "defaultSortMode": "name",
    "leftViewMode": "Full",
    "rightViewMode": "Full"
  },
  "history": {
    "maxCommandHistoryItems": 1000,
    "maxDirectoryHistoryItems": 500,
    "maxFileHistoryItems": 200
  }
}
```

Appearance can be changed from `Ctrl+S` / `F9`. Built-in palettes are `Default` and `FarClassic`; panel view modes are `Full` and `BriefTwoColumns`.

`user-menu.json` defines custom commands accessible via `F2`. Placeholder tokens:

| Token | Expands to |
|-------|-----------|
| `{current}` | Full path of the item under the cursor |
| `{selected}` | Quoted selected paths (falls back to `{current}`) |
| `{panelDir}` | Active panel directory |
| `{otherPanelDir}` | Inactive panel directory |

## Known limitations

- `CursorVisible` setter may throw in redirected-output environments (e.g. piped test runs).
- `Ctrl+Q` quick view does not refresh automatically on a background file system change — it updates on the next cursor move.
- Search (`Alt+F7`) uses `Console.KeyAvailable` for Esc-to-cancel, which is not available in redirected console environments; in that case the search runs to completion.
- The text editor does not support undo/redo.
- No mouse support.
- Windows-only (Win32 P/Invoke for screen buffer snapshot used by Ctrl+O).

## Changelog

### Stage 14 — Alt+F11 file history
- `FileHistoryItem` model added to Core
- `IHistoryStore` extended with `GetFileHistory()` / `AddFile()`
- `FileHistoryDialog` — scrollable list of recently opened files (most recent first)
- `OpenFileDialog` — asks View or Edit when a file is selected from history
- `F3` and `F4` record the opened file; `Alt+F11` shows the dialog
- 140 tests passing

### Stage 15 — Settings and portable mode
- `AppSettings` model: `ui`, `shell`, `panels`, `history` sections
- `JsonSettingsStore` — creates `settings.json` on first run; supports portable mode via `CSharpFar.portable` marker file
- `FileSystemService` — filters hidden / system files based on settings
- `JsonHistoryStore` — max item limits now come from settings
- `Application` — respects `confirmDelete`, start directories from settings
- 146 tests passing

### Stage 16 — Ctrl+Q quick view
- `QuickViewRenderer` — renders a file or directory preview in the inactive panel
- Files: shows up to N lines of text content (10 MB limit)
- Directories: shows path and item count
- `Ctrl+Q` toggles the mode; preview updates automatically on cursor movement
- 158 tests passing

### Stage 17 — Alt+F7 file search
- `FileSearcher` — recursive file search with glob mask; thread-safe, cancellable via `CancellationToken`
- `SearchProgressDialog` — background search with live count and Esc-to-cancel
- `SearchResultsDialog` — scrollable list of results; Enter navigates panel to the file
- 164 tests passing

### Stage 18 — F2 user menu
- `UserMenuItem` model: `title` + `command`
- `UserMenuStore` — loads `user-menu.json` from config directory; creates sample entries on first run
- `PlaceholderExpander` — replaces `{current}`, `{selected}`, `{panelDir}`, `{otherPanelDir}`
- `UserMenuDialog` — scrollable list showing command titles
- Selected command is run through the shell service and added to command history
- 174 tests passing

### Stage 19 — F1 help and documentation
- `HelpContent` — built-in array of help lines covering all key bindings
- `HelpViewer` — full-screen viewer with same scroll controls as the file viewer; F1/F10/Esc closes
- README updated with keyboard reference, configuration docs, build/publish instructions, known limitations
- 189 tests passing

### Stage 13 — F4 text editor
- `EditorModel` — pure editing model: `InsertChar`, `DeleteBack` (merge on line start), `DeleteForward` (merge on line end), `BreakLine`; cursor wraps across lines on Left/Right; `IsDirty` / `MarkClean`; `GetText(newLine)`
- `FileEditor` — full-screen editor; header shows `* filename row:col` when dirty; White/Black content with tab→space; footer `2Save 10Close`; `EnsureCursorVisible` scrolls to follow cursor; real blinking cursor via `SetCursorPosition`
- `SaveChangesDialog` — modal on exit with unsaved changes: `[S]ave [D]iscard [C]ancel`; S/Enter saves and exits, D discards and exits, C/Esc stays
- `TextFileReader.ReadLinesAndEncoding` — returns `(Lines, Encoding)` so editor saves with the same encoding it read
- `F4` opens editor; refreshes active panel on return; F2 saves in-place
- 137 tests passing

### Stage 12 — F3 file viewer
- `TextFileReader` — public static helper; BOM-aware encoding detection; 10 MB size limit
- `FileViewer` — full-screen viewer; tab expansion; horizontal + vertical scroll; Esc or F10 exits
- `F3` on a file opens the viewer; on directory does nothing
- 126 tests passing

### Stage 11 — directory history + Alt+F12
- Directory navigation records the new path via `AddDirectory`
- `DirectoryHistoryDialog` — scrollable list; `Alt+F12` opens it
- 121 tests passing

### Stage 10 — persisted history + Alt+F8
- `JsonHistoryStore` persists command + directory history to `%APPDATA%\CSharpFar\history.json`
- `HistoryDialog` — scrollable list; `Alt+F8` opens it
- 119 tests passing

### Stage 9 — F8 delete
- `FileOperationService.DeleteAsync` — deletes files and directories recursively
- `ConfirmDialog` — centered modal with D/Enter to confirm and C/Esc to cancel
- 107 tests passing

### Stage 8 — F6 move/rename
- Single source + plain name → rename; path destination or multiple sources → move to directory
- Conflict handling: Overwrite / Skip / Cancel
- 102 tests passing

### Stage 7 — F5 copy
- `CopyAsync` with `onProgress` and `onConflict` callbacks
- `ProgressDialog`, `ConflictDialog`, `MessageDialog` added
- 91 tests passing

### Stage 6 — F7 create folder
- `InputDialog` modal for folder name input with inline validation
- `FileOperationService.CreateDirectory`; cursor positioned on newly created folder
- 80 tests passing

### Stage 5 — File selection and sorting
- `Insert` / `Ctrl+A` / `Ctrl+*` for selection; `Ctrl+F3/F4/F5/F6` for sort
- Directories always appear before files
- 73 tests passing

### Stage 4 — Ctrl+O shell output view
- Underlay snapshot captured before first paint and after each shell command
- `Ctrl+O` restores the snapshot so shell output is visible beneath the panels
- 54 tests passing

### Stage 3 — Command line and shell execution
- `CommandLineState`, `ShellService`, `CommandLineRenderer`
- `cmd.exe /c` execution with inherited console; output captured to underlay
- 50 tests passing

### Stage 2 — Two panels with navigation
- `FileSystemService`, `PanelController`, `PanelRenderer`, `StatusBarRenderer`, `Theme`
- 31 tests passing

### Stage 1 — Console abstraction layer
- `IConsoleDriver`, `SystemConsoleDriver` (Win32 P/Invoke), `ScreenRenderer`, `FakeConsoleDriver`
- 16 tests passing

### Stage 0 — Project skeleton
- Solution with 5 source projects and 1 test project targeting `net10.0`
- Domain models and service interfaces
