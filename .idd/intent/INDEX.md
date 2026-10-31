# IDD Intent Index

This index helps humans and CodingAgents find relevant current intent documents.
It is not the source of truth.

Current numbered documents directly under `.idd/intent/` contain normative product
intent, ADRs, or active spikes.

Git history is the source for deleted or previous document versions.

The test-only input readiness contract formerly recorded in IDD-0072 lives in
[application integration input lifecycle](../../docs/testing/application-integration-input-lifecycle.md).

The durable frame-commit contract formerly recorded in IDD-0071 is now owned
by IDD-0067; the [frame lifecycle test reference](../../docs/testing/transactional-ui-frame-commit-reference.md)
is non-normative engineering history.

| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | spec | CSharpFar file manager | Product overview and Demo Mode; links to specialized owners for settings, UI, shell, packages, viewer/editor and plugins |  |
| IDD-0002 | adr | Render buffering strategy |  |  |
| IDD-0003 | spec | Display modes, palettes, and settings menu | Owns generic Settings lifecycle, display modes and palettes; Panels page and commit effects owned by IDD-0007 |  |
| IDD-0004 | spec | Column navigation, Ctrl+O command line, and resize behavior |  |  |
| IDD-0005 | spec | Drive and volume selection |  |  |
| IDD-0006 | spec | File and folder highlighting |  |  |
| IDD-0007 | spec | File panel options | Owns Panels settings page, grouping and apply effects; shared scrollbar geometry in IDD-0020 |  |
| IDD-0008 | spec | Menu bar, popup rendering, and shadows | Menu commands and visuals only; underlying-layer redraw and popup lifetime owned by IDD-0067 |  |
| IDD-0009 | spec | Open panel item |  |  |
| IDD-0010 | spec | File operations |  |  |
| IDD-0012 | spec | Search feature | Virtual result snapshots; edit and delete referenced items without making the results source writable |  |
| IDD-0013 | spec | Dynamic function key bar | Availability follows command capabilities, including search-results F4/F8 |  |
| IDD-0014 | spec | Quick View Panel |  |  |
| IDD-0016 | adr | Viewport-bound console rendering |  |  |
| IDD-0017 | spec | Current-console program launch |  |  |
| IDD-0018 | spec | Command history completion |  |  |
| IDD-0019 | spec | Reliable copy resume |  |  |
| IDD-0020 | spec | Unified window scrollbars | Shared right-border rendering; pointer interaction owned by IDD-0021 |  |
| IDD-0021 | spec | Scrollbar mouse interaction | Arrows, track paging and thumb drag; committed routing per IDD-0062/0067 |  |
| IDD-0022 | spec | Hidden console history scroll |  |  |
| IDD-0027 | spec | Far command-line shortcuts |  |  |
| IDD-0028 | spec | Panel and hidden command-line workspace modes |  |  |
| IDD-0029 | spec | SFTP file system provider |  |  |
| IDD-0030 | spec | Panel source error retry |  |  |
| IDD-0032 | spec | Unified File Viewer |  |  |
| IDD-0033 | spec | Viewer Encoding Selection |  |  |
| IDD-0034 | spec | Panel quick search |  |  |
| IDD-0035 | spec | FTP and FTPS file system provider |  |  |
| IDD-0036 | spec | Application icon | Windows host executable icon; common source artwork shared with macOS bundle IDD-0077 |  |
| IDD-0038 | spec | History-enabled text input | Persistent field history and transient popup semantics; implementation reference in docs/architecture/history-text-input-implementation-reference.md |  |
| IDD-0046 | spec | Built-in text editor | Stable editor behavior; implementation history in docs/architecture/editor-implementation-reference.md |  |
| IDD-0048 | spec | Create new file dialog |  |  |
| IDD-0049 | spec | Editor Far editing commands | Current key bindings and selection semantics; historical implementation reference in docs/architecture/ |  |
| IDD-0050 | spec | Editor TextMate syntax highlighting | Stable syntax coloring and selectors; implementation reference in docs/architecture/editor-syntax-highlighting-implementation-reference.md |  |
| IDD-0051 | spec | Editor Unicode scalar cursor behavior |  |  |
| IDD-0052 | spec | Viewer Far Hotkeys |  |  |
| IDD-0053 | spec | Delete progress dialog |  |  |
| IDD-0054 | spec | Reliable copy auto retry |  |  |
| IDD-0055 | spec | Directory shortcuts |  |  |
| IDD-0056 | spec | Built-in cd panel navigation |  |  |
| IDD-0059 | spec | Panel cursor normalization |  |  |
| IDD-0060 | spec | File rename operation |  |  |
| IDD-0061 | spec | Native plugin system |  |  |
| IDD-0062 | spec | Console UI component library | Reusable component semantics only; frame/routing/mouse capture in IDD-0067 and scrollbar mechanics in IDD-0020/0021 |  |
| IDD-0063 | spec | File attributes dialog |  |  |
| IDD-0064 | spec | Unix terminal input |  |  |
| IDD-0065 | spec | Debian/Ubuntu APT distribution and installation |  |  |
| IDD-0066 | spec | Folder and file-set comparison |  |  |
| IDD-0067 | spec | UI layer composition and modal resize | Owns committed frames, input routing, modal focus/resize; terminal-shell fallback separately in IDD-0022 |  |
| IDD-0073 | spec | Processes and Ports | Local endpoint diagnostics module |  |
| IDD-0074 | spec | FAR-compatible wildcard destination names | Destination name transformation for file operations |  |
| IDD-0075 | spec | Destination templates | Explicit per-source destination path evaluation for file operations |  |
| IDD-0076 | spec | macOS Homebrew distribution | Formula/Cask artifacts, checksums, tap and metadata publication; install/update orchestration owned by IDD-0082 |  |
| IDD-0077 | spec | macOS application bundle | Owns bundle contents, desktop identity and host lifecycle; package metadata in IDD-0076 |  |
| IDD-0080 | spec | File Usage panel | Local-file usage and lock diagnostics with safe owner release |  |
| IDD-0081 | spec | Runtime diagnostics | Opt-in in-memory tracing and safe copyable diagnostic report |  |
| IDD-0082 | spec | macOS install and self-update | Sole owner of installer, quarantine, update eligibility, cancellation and relaunch; uses the Cask in IDD-0076 | IDD-0076/IDD-0077 distribution workflow clarification |
| IDD-0083 | spec | User menu | F2 command selection, Options editor, platform filters, placeholders, and user-menu.json persistence |  |
| IDD-0084 | adr | macOS embedded terminal engine | Pinned SwiftTerm AppKit/PTY host and CLI compatibility |  |
| IDD-0086 | spec | Network Traffic | Live top-consumer native plugin |  |
