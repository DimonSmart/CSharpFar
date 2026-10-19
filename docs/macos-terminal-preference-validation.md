# macOS preferred terminal — desktop acceptance matrix

The implementation and cross-platform automated contract tests are independent
of a window server. The scenarios below require an actual macOS desktop session
on Apple Silicon; verify Intel when available.

**Status for this change:** NOT EXECUTED — this implementation was authored
remotely and no macOS Finder/WindowServer or Kitty GUI session was available.
Do not interpret a passing unit test, successful `Process.Start`, or a zero exit
code from `/usr/bin/open` as proof that CSharpFar is interactive inside Kitty.

Record separately whether (a) the expected terminal window opened and
(b) CSharpFar was running and accepting keyboard input.

| Scenario | Window opened | Interactive CSharpFar |
| --- | --- | --- |
| Automatic preference, Kitty installed | Not verified | Not verified |
| Explicit Kitty preference, Kitty installed | Not verified | Not verified |
| Terminal.app preference, Kitty installed | Not verified | Not verified |
| Automatic, Kitty absent | Not verified | Not verified |
| Kitty preference, Kitty absent (preference retained) | Not verified | Not verified |
| Kitty already running (no duplicate or empty session) | Not verified | Not verified |
| Kitty custom startup_session, `--session none` | Not verified | Not verified |
| Kitty custom macos-launch-services-cmdline | Not verified | Not verified |
| Bundle moved to directory with spaces/Unicode | Not verified | Not verified |
| Kitty installed at `~/Applications/kitty.app` | Not verified | Not verified |
| Existing TTY: bundle launcher with no arguments | Not verified | Not verified |
| Direct binary in Kitty, Terminal, IDE, SSH | Not verified | Not verified |
| CLI --version, --self-test, --check-terminal, input-lab | Not verified | Not verified |
| Save vs Cancel, Finder/Dock/Spotlight subsequent launch | Not verified | Not verified |
| Kitty installed/removed between app launches | Not verified | Not verified |
| Homebrew in-app update and `open -n` relaunch | Not verified | Not verified |
| Short session exit, focus, Dock icon and process lifecycle | Not verified | Not verified |
| Kitty Ctrl+Enter, Shift+Enter, Ctrl+Shift+Enter, modifier lifecycle | Not verified | Not verified |

For smoke testing, use a real PTY for CLI checks, then launch the bundle
without a TTY via Finder. The `Contents/MacOS/CSharpFar --version` packaging
smoke test does **not** exercise provider selection. Do not assume a Kitty
bundle placed outside the two recognized Applications directories is found:
Launch Services discovery is out of scope for this version.

## Finder child PATH — additional manual acceptance matrix

**Status: NOT EXECUTED.** These cases require an actual macOS GUI session
with the locally installed PowerShell Preview and user shell configuration.
Check both Apple Silicon and Intel if available. Document the executed machine,
terminal preference, shell, outcome, and any probe fallback separately.

| Scenario | Kitty Finder | Terminal.app Finder | Direct CLI |
| --- | --- | --- | --- |
| `pwsh` available only from `~/.zshrc` in `auto` | Not verified | Not verified | Not verified |
| PATH configured in `~/.zprofile` | Not verified | Not verified | Not verified |
| `ps:` and `pwsh:` PowerShell prefixes | Not verified | Not verified | Not verified |
| Executable opened from panel and its child tool | Not verified | Not verified | Not verified |
| PATH precedence, duplicate, empty, Unicode and spaced directories | Not verified | Not verified | Not verified |
| Broken/slow user shell produces fallback, not a hung app | Not verified | Not verified | Not verified |
| `appendStandard` skips user shell initialization | Not verified | Not verified | Not verified |
| `inherited` skips user shell initialization | Not verified | Not verified | Not verified |
| Legacy settings auto migration and explicit `appendStandard` afterwards | Not verified | Not verified | Not verified |
| PATH diagnostics show mode, source, shell, duration and fallback | Not verified | Not verified | Not verified |

To reproduce the original regression, install PowerShell Preview with `pwsh`
at `/usr/local/microsoft/powershell/7-preview/pwsh`; place its directory
**only** in `~/.zshrc`, not in global launchd PATH. Start the app from
Finder, first with Kitty and then with Terminal.app. Run `pwsh` and
`pwsh: $PSVersionTable.PSVersion`. The expected diagnostic PATH source is
`user shell` and the shell is probed once per app session. Repeat via the
direct CLI and confirm no probe takes place.
