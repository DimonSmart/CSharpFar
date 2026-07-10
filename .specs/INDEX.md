# Specs Index

| ID | Type | Status | Title | Summary |
|----|------|--------|-------|---------|
| 0001 | Spec | Current | CSharpFar file manager | CSharpFar is a console two-panel file manager written in C# for .NET 10. Its primary user model follows Far Manager: two file panels, a c... |
| 0002 | ADR | Accepted | Render buffering strategy | Console UI redraw must avoid flicker and unnecessary physical writes without making each UI component track dirty regions. Rendering also... |
| 0003 | Spec | Current | Display modes, palettes, and settings menu | CSharpFar needs configurable panel display modes, configurable console palettes, and an in-application settings dialog so users can chang... |
| 0004 | Spec | Current | Column navigation, Ctrl+O command line, and resize behavior | Multi-column panels, hidden-panel mode, shell command output, and console resize must behave predictably on the main screen. |
| 0005 | Spec | Current | Drive and volume selection | Users need a Far-like way to switch a panel to another drive, volume, or file system root without typing paths manually. |
| 0006 | Spec | Current | File and folder highlighting | File panels need configurable visual highlighting so users can distinguish directories, file types, masks, attributes, and special entrie... |
| 0007 | Spec | Current | File panel options | File panels expose Far-like display and behavior options. These options affect which items are visible, how they are sorted, what summary... |
| 0008 | Spec | Current | Menu bar, popup rendering, and shadows | CSharpFar needs a top menu bar, drop-down menus, and shared popup/dialog rendering behavior so modal UI elements have consistent layout,... |
| 0009 | Spec | Current | Open panel item | Users open directories and files from panel items with `Enter` and mouse actions. Single-click selection must remain distinct from openin... |
| 0010 | Spec | Current | File operations | CSharpFar file operations must be predictable for everyday Far-like workflows: copy, move, delete, and create directory. Operations need... |
| 0012 | Spec | Current | Search feature | Add file, directory, and text search to CSharpFar. Search starts from the active file panel, collects a normalized request, runs as a can... |
| 0013 | Spec | Current | Dynamic function key bar | Make the bottom function key bar a real part of the command and key binding system. The bar must show the actions that are available for... |
| 0014 | Spec | Current | Quick View Panel | Provide an enhanced Quick View panel (Ctrl+Q) that shows rich, context-aware previews of the currently selected item in the opposite pane... |
| 0016 | ADR | Accepted | Viewport-bound console rendering | The buffered rendering strategy from `0002` reduces flicker by building a full logical frame and flushing only changed horizontal runs. W... |
| 0017 | Spec | Current | Current-console program launch | Change external program launch behavior so executable files and typed commands run like Far Manager: the child console process writes dir... |
| 0018 | Spec | Current | Command history completion | Improve command-line history so executed commands are stored once, repeated commands move to the top of history, and command entry can us... |
| 0019 | Spec | Current | Reliable copy resume | Add a copy mode named `Reliable` in the copy start dialog. The mode must be available before copying begins, including recursive folder c... |
| 0020 | Spec | Current | Unified window scrollbars | Use the shared scrollbar component for every scrollable window-like UI surface. Scrollbars should appear on the right window border,... |
| 0021 | Spec | Current | Scrollbar mouse interaction | Make the unified vertical scrollbar respond to standard mouse actions. |
| 0022 | Spec | Current | Hidden console history scroll | When panels are hidden through `Ctrl+O`, the user can scroll the console viewport up to inspect older command output. CSharpFar must not... |
| 0027 | Spec | Current | Far command-line shortcuts | Add Far-like command-line insertion shortcuts for panel items, panel paths, and command history navigation. |
| 0028 | Spec | Current | Panel visibility modes | Let the left and right file panels be hidden independently, while keeping `Ctrl+O` as the shortcut for hiding or showing both panels at o... |
| 0029 | Spec | Current | SFTP file system provider | Add a provider-based panel source model and a built-in SFTP source so a user can connect to an SFTP server, browse remote directories in... |
| 0030 | Spec | Current | Panel source error retry | Show panel-source read errors inside the affected panel and let the user retry the failed panel refresh after the external problem is fixed. |
| 0032 | Spec | Current | Unified File Viewer | Use one F3 viewer implementation for every file and let the user switch between text and hex display modes. |
| 0033 | Spec | Current | Viewer Encoding Selection | Let the F3 viewer decode text with either automatic encoding detection or a user-selected encoding while keeping the unified streaming vi... |
| 0034 | Spec | Current | Panel quick search | Add Far-like quick search for the active file panel. When the main file manager UI is active, pressing `Alt` plus the first filename char... |
| 0035 | Spec | Current | FTP and FTPS file system provider | Add a built-in FTP/FTPS panel source so a user can connect to an FTP server, browse remote directories in a normal panel, and use the exi... |
| 0036 | Spec | Current | Application icon | Give CSharpFar its own Windows application icon with a Far-like file manager identity without copying Far Manager branding or artwork. |
| 0038 | Spec | Current | History-enabled text input | Add optional command-history-style suggestions to reusable single-line text input controls and define reusable control mouse expectations. |
| 0046 | Spec | Current | Built-in text editor | Turn the current F4 editor into a predictable Far-like text editor with a real internal editor model, explicit file format handling, undo... |
| 0048 | Spec | Current | Create new file dialog | Add a Far-like `Shift+F4` command that creates a new text file from the file panels and opens it in the built-in editor. The command must... |
| 0049 | Spec | Current | Editor Far editing commands | Add the remaining Far-like editor commands from the implementation analysis as native CSharpFar editor behavior. The work should extend t... |
| 0050 | Spec | Current | Editor TextMate syntax highlighting | Add syntax highlighting to the built-in CSharpFar editor using TextMate grammars. The first implementation should color visible editor te... |
| 0051 | Spec | Current | Editor Unicode scalar cursor behavior | The built-in editor stores text as .NET strings, so internal positions are UTF-16 indexes. Unicode scalar values outside the Basic Multil... |
| 0052 | Spec | Current | Viewer Far Hotkeys | Make the F3 file viewer accept the main Far Manager viewer keys so users can navigate, switch modes, search, change encoding, edit, and m... |
| 0053 | Spec | Current | Delete progress dialog | Show file deletion with a delete-specific progress dialog instead of reusing the copy progress layout. |
| 0054 | Spec | Current | Reliable copy auto retry | Make `Reliable` copy automatically retry read-side and destination write failures without showing a modal wait prompt. |
| 0055 | Spec | Current | Directory shortcuts | Add Far-like directory shortcuts so the user can configure up to ten numbered directories and quickly navigate the active visible panel t... |
| 0056 | Spec | Current | Built-in cd panel navigation | Handle `CD` and `CHDIR` typed in the built-in command line as CSharpFar built-in commands and navigate the active local file panel to the... |
| 0057 | Spike | Open | Unix and macOS portability | What must change for CSharpFar to run predictably on Unix-like systems, especially Linux and macOS, without hiding platform differences b... |
| 0059 | Spec | Current | Panel cursor normalization | Keep file panel cursor and scroll state valid after panel item lists are refreshed, rebuilt, or sorted. |
| 0060 | Spec | Current | File rename operation | Make renaming the current panel item a distinct user command. Plain `F6` remains primarily move; `Shift+F6` is the direct rename shortcut. |
| 0061 | Spec | Current | Native plugin system | CSharpFar supports native .NET plugins designed around CSharpFar internal abstractions and intentionally does not emulate FarNet or Far Manager plugin APIs. |
| 0062 | Spec | Current | Console UI component library | CSharpFar owns an internal reusable console UI component library for rendering, popup/dialog frames, scrollbars, text input, button bars, selection lists,... |
| 0063 | Spec | Current | File attributes dialog | CSharpFar provides a Far-like local file metadata dialog for viewing and changing provider-supported attributes and file times through capability-based metadata services. |
| 0064 | Spec | Current | Unix terminal input | Unix hosts use one raw VT keyboard, SGR mouse, and resize input backend with explicit terminal lifecycle restoration. |
| 0065 | Spec | Current | Debian/Ubuntu APT distribution and installation | CSharpFar publishes Debian packages through a signed GitHub Pages APT repository and provides a one-command Debian/Ubuntu installer with manual APT fallback. |
| 0066 | Spec | Current | Folder and file-set comparison | Add read-only comparison commands for local panel folders: folder structure mode matches by relative path, while file-set mode ignores folder structure and handles duplicates explicitly. |
| 0067 | Spec | Current | UI layer composition and modal resize | Compose the active full-screen surface and modal overlays through one shared host so resize redraws current state rather than restoring snapshots. |
| 0068 | Spec | Superseded by 0069 | Complete UI composition migration | Replaced by the corrective completion specification after the implementation audit found remaining legacy UI lifecycles. |
| 0069 | Spec | Superseded by 0070 | Finish UI composition migration | Complete the remaining application-owned UI migration and enforce one stable composition/input lifecycle through source-contract and buffer-level tests. |
| 0070 | Spec | Current | Complete resize composition migration | Complete the composition migration so all application-owned UI redraws current state at the current viewport without snapshot restoration. |
