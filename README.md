# CSharpFar

A console dual-panel file manager for Windows, inspired by Far Manager.

Built with C# and .NET 10.

## Current status

**Stage 1 complete** — console abstraction layer implemented.

The application draws a two-panel placeholder UI using `IConsoleDriver` / `ScreenRenderer` and waits for a keypress.

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

## Known limitations

- No file panels yet — the application shows a placeholder box UI and exits on any key.
- Shell service not implemented; file operations not implemented.
- `CursorVisible` setter may throw in redirected-output environments.
