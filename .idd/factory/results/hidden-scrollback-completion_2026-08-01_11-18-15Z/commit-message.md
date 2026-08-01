Preserve hidden scrollback and show command completion

Performed by: IDD Factory

Why:
Hidden terminal scrollback was being treated as a resize, which pulled users
back to the bottom. Command-history completion also needed to be usable without
visible panels while preserving shell output.

Result:
- Accept hidden origin-only viewport moves without rendering or bottom scrolling.
- Resume hidden rendering only for meaningful handled application interaction.
- Show shared command-history completion in panels and hidden command-line mode.
- Restore shell output safely beneath hidden command-line and completion overlays.
- Keep completion and terminal transitions free of stale popup artifacts.
