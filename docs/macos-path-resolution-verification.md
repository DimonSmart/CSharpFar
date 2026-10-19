# macOS user-shell PATH: implementation and verification

## Implementation

- Finder/Kitty marks its CSharpFar process via `ProcessStartInfo.Environment`;
  Finder/Terminal.app uses `launch.command` to export the equivalent marker.
- `auto` obtains the PATH once through the user's supported login interactive
  shell (zsh or bash), with separated markers, bounded stdout/stderr, a 3-second
  process-tree timeout, and safe fallback. Direct CLI starts skip the probe.
- One provider shares the same cached result across shell commands, PowerShell
  aliases, and executable panel processes.
- Existing legacy `appendStandard` and `inherited` semantics remain explicit
  opt-outs; old serialized `appendStandard` values migrate once using a version
  marker. Full shell output and PATH values are not logged.

## Verification status

Unit tests added: isolated shell-probe protocol, noise, invalid output, failure,
unsupported shells, timeout, synthetic Finder/CLI decision, PATH normalization,
parallel caching, settings migration, launcher hint, PowerShell and Unix builder
PATH propagation. A macOS-only test uses a temporary `ZDOTDIR` for both
`.zprofile` and `.zshrc`.

Automated CI: pending at authoring time (the editing environment did not have
a .NET SDK installed, and cannot execute `dotnet test` locally).

Manual GUI checks: **not executed**. The [desktop matrix](macos-terminal-preference-validation.md)
tracks Finder/Kitty, Finder/Terminal.app, both architectures, and the original
PowerShell Preview reproduction. Completion of automated tests cannot be used as
proof that those desktop scenarios passed.
