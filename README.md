# CSharpFar

CSharpFar is an experimental file manager written in C#.

The goal is simple: rebuild the familiar Far Manager experience in a modern C# codebase, then improve it where modern tooling and LLM-assisted development make that practical.

![CSharpFar two-panel file manager preview](docs/images/csharpfar-preview.svg)

<!--
Replace the generated preview with real application screenshots when they are ready, for example:

![CSharpFar main window](docs/images/csharpfar-main.png)
![CSharpFar settings window](docs/images/csharpfar-settings.png)
-->

## Why

Far Manager is fast, keyboard-friendly and works well as a shell-oriented file manager. CSharpFar keeps that direction, but uses C# and .NET as the implementation base.

The project is not only about copying the old UI. The interesting part is to keep the speed and predictability of a classic two-panel file manager while making the codebase easier to extend, test and evolve with modern development tools.

## Direction

CSharpFar is expected to grow around a few core ideas:

- a Far-like two-panel console interface;
- fast keyboard-first file navigation;
- common file operations such as copy, move, delete, view and edit;
- command-line usage with command history;
- Windows file opening through system file associations;
- mouse support where it helps, without turning the app into a GUI clone;
- configurable visual modes and palettes.

## LLM-assisted development

This repository uses a worklog-driven development process.

The `.worklog` directory is used to keep task notes, decisions, experiments and implementation history. This gives LLM coding tools enough project context to continue work without treating every request as a fresh one.

The methodology will be described in a separate article later. After publication, the link should be added here.

## Viewer

`F3` opens a full-screen viewer for the selected file.

The viewer uses one streaming path for small and large files. It reads fixed-size byte
blocks by offset and keeps only a small LRU cache, so opening a log does not require
loading the whole file first.

Text-looking files open as text. Binary-looking files open as a 16-byte-per-row hex dump.
Press `H` to switch the current file between text and hex.

The viewer supports:

- `Home` and `End` for start/end navigation;
- `Up`, `Down`, `PageUp`, and `PageDown`;
- horizontal scrolling with `Left` and `Right`;
- `G` for a line number or percent jump, for example `12000` or `85%`;
- `F` follow mode for files that keep growing;
- `H` for text/hex display mode.

Text decoding detects UTF-8 and UTF-16 BOMs, tries UTF-8 without a BOM, and falls back to
the same default encoding used by the rest of the viewer code. Damaged byte sequences are
shown with replacement characters instead of closing the viewer. Control characters from
file content are replaced before drawing, so escape sequences and similar bytes are shown
as inert text instead of being sent to the console as controls.

Quick View (`Ctrl+Q`) is still a bounded preview. It does not try to stream or scroll large
files.

## File operations

### Paranoid copy

When copying files, the copy dialog has a `Paranoid` conflict mode.

If a destination file already exists and is shorter than the source, for example after an interrupted copy, CSharpFar does the following:

1. Compares the tail of the existing destination bytes against the same region of the source.
2. If the tail matches, copying resumes from that offset. No data already on disk is rewritten.
3. If the tail is corrupted, the overlap is rolled back to the last confirmed good position, then copying continues from there.
4. If the destination cannot be matched to the source at all (unrelated file, wrong size), the normal conflict dialog is shown so the user decides.

This mode is for large copies interrupted by power loss, network drops or process termination. Equal-size and larger destination files are not resumed automatically.

## Status

Early development.

The current focus is to build the core user experience first: navigation, panels, shell-like behavior and the visual style that makes the application feel close to Far Manager.

## Build

Requirements:

- .NET SDK
- Windows terminal environment

From the repository root:

```bash
dotnet build
```

Run instructions may change while the project structure is still evolving.

## Contributing

This is an experimental project, so the best contributions are practical and specific:

- bug reports with clear reproduction steps;
- screenshots of visual differences from Far Manager;
- small focused pull requests;
- ideas that preserve the keyboard-first workflow.

Before large changes, create or update a worklog entry so the decision history stays readable.

## License

See the repository license file.
