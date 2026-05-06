# CSharpFar

A console dual-panel file manager for Windows, inspired by Far Manager.

Built with C# and .NET 10.

## Current status

**Stage 4 complete** — Ctrl+O shell output view.

The application shows two file panels with keyboard navigation, a live command line, shell command execution via `cmd.exe`, and Ctrl+O to toggle between panels and the last shell output.

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

## Known limitations

- History not persisted to disk yet — that is Stage 10.
- `CursorVisible` setter may throw in redirected-output environments.
