# CSharpFar

A console dual-panel file manager for Windows, inspired by Far Manager.

Built with C# and .NET 10.

## Current status

**Stage 9 complete** — F8 delete.

The application shows two file panels with navigation, a live command line, shell execution, Ctrl+O shell output view, file selection, sorting, F7 create folder, F5 copy, F6 move/rename, and F8 delete with confirmation.

## Requirements

- .NET 10 SDK
- Windows (primary target platform)

## Build & run

```bash
dotnet build
dotnet run --project src/CSharpFar.App
```

## Test

```bash
dotnet test
```

## Publish

```bash
dotnet publish src/CSharpFar.App/CSharpFar.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

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
  /CSharpFar.Tests      — xUnit test project
```

## Changelog

### Stage 0 — Project skeleton
- Created solution with 5 source projects and 1 test project targeting `net10.0`
- Enabled nullable reference types across all projects
- Added domain models: `FilePanelItem`, `FilePanelState`, `SortMode`, `PanelSide`,
  `CommandHistoryItem`, `DirectoryHistoryItem`
- Added service interfaces: `IFileSystemService`, `IFileOperationService`,
  `IShellService`, `IHistoryStore`, `ISettingsStore`
- Added `.editorconfig`
- Added smoke tests for core models

### Stage 1 — Console abstraction layer
- Added `IConsoleDriver` interface with `WriteAt`, `ClearRegion`, `SetCursorPosition`,
  `SetCursorVisible`, `Capture`, `Restore`
- Added `SystemConsoleDriver` — real implementation using `System.Console`; on Windows
  uses `ReadConsoleOutput` / `WriteConsoleOutput` (Win32 P/Invoke) for `Capture`/`Restore`,
  laying the groundwork for Ctrl+O shell-output preservation
- Added models: `ConsoleSize`, `Rect`, `CellStyle`, `SnapshotCell`, `ScreenSnapshot`
- Added `ScreenRenderer` — higher-level drawing surface (text, fill, box borders)
- Added `FakeConsoleDriver` for unit testing (in-memory buffer, key queue, inspection helpers)
- 16 tests passing (8 driver tests + 5 renderer tests + 3 smoke tests)

### Stage 2 — Two panels with navigation
- Added `FileSystemService` — reads directories (sorted: dirs first, then files by name)
- Added `PanelController` (CSharpFar.Core) — cursor movement, page navigation, directory entry,
  GoToParent with automatic cursor positioning on the child we came from
- Added `Application` — main input loop
- Added `PanelRenderer` — draws a panel with border, embedded path header, file list, item count footer
- Added `StatusBarRenderer` — function key bar at the bottom
- Added `Theme` — Far-like classic blue color scheme
- Keyboard: `↑↓`, `PgUp/PgDn`, `Home/End`, `Enter` (enter dir), `Backspace` (parent dir),
  `Tab` (switch panel), `F10` (quit)
- 31 tests passing (15 PanelController + 8 driver + 5 renderer + 3 smoke)

### Stage 3 — Command line and shell execution
- Added `CommandLineState` — character buffer with cursor, insert/delete/move operations
- Added `ShellService` — executes `cmd.exe /c <command>` with inherited console (output visible)
- Added `InMemoryHistoryStore` — in-memory command and directory history with duplicate suppression
- Added `CommandLineRenderer` — renders the command line with scrolling text when input exceeds width
- Command execution flow: restore shell underlay → show prompt → run command → capture output → refresh panels
- `PanelController.RefreshDirectory` — reloads directory while preserving cursor position by name
- Key routing: printable chars → command line; arrows → panel navigation; Enter → execute or enter dir
- `Escape` clears the command line; `Backspace` on empty line goes to parent directory
- 50 tests passing

### Stage 4 — Ctrl+O shell output view
- `_panelsVisible` flag gates `Render()` in the main loop; toggled by `Ctrl+O`
- `_underlay` (`ScreenSnapshot?`) stores the last captured screen before panels were drawn
- `CaptureUnderlay()` called (a) once at startup before first paint, (b) after each shell command before panels redraw
- `TogglePanels()`: hide → `Restore(_underlay)` shows last shell output; show → main loop calls `Render()`
- Panel navigation state (`FilePanelState`) is untouched while panels are hidden — cursor and directory preserved
- `Ctrl+O` checked before printable-char routing so `O` can still be typed in the command line
- 54 tests passing (50 previous + 4 underlay snapshot tests)

### Stage 5 — File selection and sorting
- `Insert` toggles selection of the current item and advances cursor; `..` is not selectable
- `Ctrl+A` selects all non-parent items, or deselects all if everything is already selected
- `Ctrl+*` inverts selection on the active panel; `Ctrl+Numpad *` and `Ctrl+Shift+8` are accepted
- Windows console input mode is adjusted while the app runs so `Ctrl+A` reaches CSharpFar instead of selecting console text
- `Ctrl+F3` sort by name, `Ctrl+F4` by extension, `Ctrl+F5` by last write time, `Ctrl+F6` by size
- Pressing the same sort key again reverses the sort direction
- Directories always appear before files in every sort mode
- Selected items shown in yellow in both active and inactive panels
- Footer shows selected count when items are selected; otherwise shows total count
- 73 tests passing

### Stage 6 — F7 create folder
- `InputDialog` — centered modal box (44×6): title in border, prompt label, scrollable input field, error row
- `FileOperationService.CreateDirectory` — throws `IOException` if folder already exists
- `PanelController.SetCursorByName` — positions cursor on a named item after refresh
- `F7` opens the dialog; on confirm: creates folder, refreshes panel, positions cursor on new folder
- Error shown inline (folder exists, access denied, invalid chars) — user can retry or Esc to cancel
- `CSharpFar.Tests` now references `CSharpFar.FileSystem` for integration tests
- 80 tests passing

### Stage 7 — F5 copy
- `ConflictChoice` enum: Overwrite / Skip / Cancel
- `IFileOperationService.CopyAsync` updated: accepts `onProgress` and `onConflict` callbacks
- `FileOperationService.CopyAsync` — copies files and directories recursively; conflict callback decides each clash
- `ProgressDialog` — non-modal overlay showing destination and current filename during copy
- `ConflictDialog` — modal box: `[O]verwrite [S]kip [C]ancel`; `Esc` also cancels
- `MessageDialog` — simple modal for error messages (reusable in later stages)
- `InputDialog` now accepts optional `initialText` for pre-filled destination field
- Sources: selected items if any, otherwise current item; `..` is never a source
- After copy: both panels refresh, active panel selection is cleared
- 91 tests passing

### Stage 8 — F6 move/rename
- `IFileOperationService.MoveAsync` updated: accepts optional `onConflict` callback
- Single source + plain name (no path separators) → rename in-place
- Path destination or multiple sources → move to specified directory
- Conflict handling: Overwrite deletes destination then moves; Skip leaves source unchanged; Cancel throws
- Pre-fill: single item → current name (for easy rename); multiple items → opposite panel directory
- Both panels refresh and selection cleared after operation
- 102 tests passing

### Stage 9 — F8 delete
- `FileOperationService.DeleteAsync` — deletes files and directories recursively; silently skips non-existent paths
- `ConfirmDialog` — centered modal (52×5): title, prompt line, `[D]elete   [C]ancel` buttons; D/Enter confirms, C/Esc cancels
- `F8` opens confirm dialog; on confirm: deletes sources, refreshes both panels, clears selection
- Errors (access denied, etc.) shown via `MessageDialog` — do not crash the app
- 107 tests passing

## Known limitations

- History not persisted to disk yet — that is Stage 10.
- `CursorVisible` setter may throw in redirected-output environments.
