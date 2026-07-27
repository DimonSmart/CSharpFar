# Features

CSharpFar is built around a keyboard-first, two-panel workflow inspired by Far Manager, while using a modern C#/.NET implementation and native cross-platform abstractions.

## Panels and navigation

- Two-panel file-system navigation.
- Keyboard-first operation with mouse support where it improves usability.
- Command-line workflow and command history.
- File menus, attributes, navigation and common file-management commands.
- Platform-specific behavior for Windows and Linux where the operating systems differ.

## File operations

CSharpFar supports the usual copy, move, delete and directory-management workflows, including progress dialogs, conflict handling and cancellation.

### Reliable copy

Reliable copy is intended for large transfers that may be interrupted by network failures, power loss or process termination.

When a shorter destination file already exists, CSharpFar can compare the existing tail with the corresponding source region and resume from the confirmed position instead of rewriting data that is already safely stored. If the tail is damaged, the operation can roll back to the last confirmed position before continuing.

Unrelated, equal-size or otherwise unsafe destination states are not silently treated as resumable copies; normal conflict handling remains in control.

## Viewer and editor

CSharpFar includes a built-in streaming file viewer and a text editor.

The viewer supports text and hexadecimal modes, large files, searching, wrapping, multiple encodings, follow mode and direct navigation by line, byte offset or percentage.

The editor includes modern text handling and syntax highlighting while retaining a terminal-oriented interaction model.

See [Viewer and editor](viewer-and-editor.md) for details.

## Search and comparison

- File search integrated into the panel workflow.
- Folder comparison by relative path.
- File-set comparison independent of directory structure.
- Comparison by metadata, size or content where appropriate.
- Streaming content hashing for file-set content matching.
- Explicit handling of ambiguous duplicate matches instead of silently pairing them.

## Remote file systems

First-party modules provide remote file-system access for:

- FTP / FTPS;
- SFTP.

They integrate with the same panel-oriented workflow rather than acting as separate transfer utilities.

## Native plugins

CSharpFar has its own plugin API rather than relying on Far Manager compatibility layers.

The plugin model provides abstractions for commands, menu items, panel providers, context, settings and UI services. FTP and SFTP are first-party examples of functionality built on this extensibility model.

## Windows and Linux

The solution has separate Windows and Unix hosts and platform implementations behind shared abstractions.

Platform-specific functionality includes terminal input/output, process launching, file-system metadata and permissions. On Linux, the file attributes workflow supports POSIX permission bits and owner/group-aware metadata where available.

## UI engine

CSharpFar uses a custom terminal UI subsystem rather than a general-purpose GUI toolkit. It handles layered composition, modal dialogs, focus and mouse routing, committed frame state, scrolling, editor/viewer surfaces and fine-grained invalidation for partial redraws.

These implementation details are intentionally not part of the main README, but they make the project useful as a realistic engineering sandbox in addition to being a file manager.
