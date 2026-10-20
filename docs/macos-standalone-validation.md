# macOS standalone desktop host — manual acceptance

The desktop host is an AppKit application embedding a SwiftTerm VT/PTY view.
The CLI is a separate self-contained executable inside the app bundle.
`IDD-0077` defines the intended behavior and `IDD-0084` records the
SwiftTerm selection.

**Manual acceptance status: NOT EXECUTED.** A macOS desktop/WindowServer
session with user input and both supported architectures is required.
Successful CLI checks, package compilation, or a successful macOS `open`
request do **not** establish that the embedded terminal behaves correctly.

| Scenario | Apple Silicon | Intel |
| --- | --- | --- |
| Open from Finder and Applications, identity in Dock and Cmd+Tab | Not verified | Not verified |
| Launch from Spotlight and Launchpad | Not verified | Not verified |
| Dock activation, pin/quit/reopen, normal second open | Not verified | Not verified |
| Explicit second instance with `open -n` | Not verified | Not verified |
| Kitty absent or independently running | Not verified | Not verified |
| Legacy `preferredMacOsTerminal` values auto/kitty/terminal | Not verified | Not verified |
| Panels, menus, dialogs, editor, viewer, file operations | Not verified | Not verified |
| Unicode, combining text, IME commit and clipboard paste/copy | Not verified | Not verified |
| Kitty profile 27 press/repeat/release, modifier-only events | Not verified | Not verified |
| Focus loss while Shift/Alt/Control held, no stuck keys | Not verified | Not verified |
| Mouse clicks, double-click, motion, wheel, dragging | Not verified | Not verified |
| Ctrl+O terminal scrollback and main-screen selection | Not verified | Not verified |
| Resize, maximize, fullscreen and display scale changes | Not verified | Not verified |
| Large output and alternate-screen full-screen child | Not verified | Not verified |
| Foreground Ctrl+C; shell and panels restored | Not verified | Not verified |
| F10, Cmd+Q, window close with unsaved editor | Not verified | Not verified |
| Close with running command: confirm or cancel | Not verified | Not verified |
| Unexpected core exit, startup failure, no orphan processes | Not verified | Not verified |
| Cask install, update and new standalone host relaunch | Not verified | Not verified |

Automated checks run in [macOS standalone host workflow](../.github/workflows/macos-standalone-host.yml)
and in the tagged Release workflow. The automated checks are expected to verify:
.NET regression tests; SwiftTerm source compilation on native arm64/x64 runners;
bundle identifier, version, host and CLI architecture; CLI binary parity;
no-window diagnostics and shell/PTY diagnostic behavior.

Useful manual smoke commands:

```bash
/Applications/CSharpFar.app/Contents/MacOS/CSharpFar --version
/Applications/CSharpFar.app/Contents/MacOS/CSharpFar --check-system-git
/Applications/CSharpFar.app/Contents/Resources/csharpfar --check-terminal --input-lab
open /Applications/CSharpFar.app
open -n /Applications/CSharpFar.app
```

For input-laboratory checks use an actual interactive terminal session, not a
Finder process without a TTY. Confirm the keyboard enhancement flags and
modifier lifecycle within the embedded desktop window separately.

Record observed success, failure, OS/architecture, keyboard layout and window
conditions when executing these tests. Do not mark unobserved cases as passed.
