# macOS user-shell PATH: implementation and verification

## Implementation

- The standalone AppKit/SwiftTerm desktop host marks the bundled CLI PTY process
  with `CSHARPFAR_FINDER_LAUNCH=1`. Direct CLI/TTY launches do not set it.
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

Automated CI runs the macOS and cross-platform .NET tests. The merge requires
a fresh CI result; automated checks are not a substitute for GUI acceptance.

Manual GUI checks: **not executed**. The [standalone host matrix](macos-standalone-validation.md)
tracks the embedded host, both architectures, direct CLI behavior, and the
original PowerShell Preview reproduction. Automated tests do not establish
that these desktop scenarios passed.
