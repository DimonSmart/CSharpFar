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
