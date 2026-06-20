# Unix / WSL keyboard and mouse input check

## Goal

Evaluate xterm SGR mouse input as a diagnostic transport on Unix-like terminals,
especially Windows Terminal plus WSL, without changing the production Unix input
path.

## Current implementation

The Unix production driver still reads keyboard events through
`Console.ReadKey(intercept: true)`. The new mouse mode is diagnostic-only. It
uses `ReadRawInput()` as a byte transport and applies a separate internal SGR
mouse parser before reporting the existing `MouseConsoleInputEvent` model.

Mouse reporting is enabled after entering the application screen and disabled
in `finally` before terminal restoration. Each run writes the same diagnostic
events to the screen and to a timestamped file under the system temporary
directory.

## Test command

```bash
dotnet run --project src/CSharpFar.Host.Unix -- --check-terminal --mouse-input
```

The command must be run directly in a real terminal for physical mouse testing.
Redirected stdin or stdout intentionally skips the check.

## Environment

Automated checks on 2026-06-20 used:

```text
Windows version: 10.0.26200.8655
Windows Terminal version: 1.24.11321.0
Windows Terminal Preview version: 1.25.1322.0
WSL version: 2.6.3.0
WSL kernel: 6.6.87.2-1
Distribution: Ubuntu 24.04.4 LTS
Architecture: x64
TERM: xterm-256color
COLORTERM: unset in the automated PTY
WT_SESSION: unset in the automated PTY
Shell: GNU bash 5.2.21
tmux: 3.4 installed, not physically tested
.NET SDK: 10.0.109
.NET runtime: 10.0.9
```

The automated run used a Codex-controlled pseudo-terminal, not an interactive
Windows Terminal tab. Therefore the installed Windows Terminal versions do not
identify the frontend that generated the PTY data.

## Mouse protocol tested

The diagnostic enables xterm cell-motion tracking and SGR extended coordinates:

```text
ESC[?1002h
ESC[?1006h
```

It disables both modes on normal or exception exit:

```text
ESC[?1002l
ESC[?1006l
```

The parser accepts `CSI < Cb ; Px ; Py M/m`, converts `Px` and `Py` to zero-based
coordinates, decodes Shift/Alt/Control, and distinguishes button down/up, wheel,
and drag motion. Protocol details follow the
[xterm control-sequence reference](https://invisible-island.net/xterm/ctlseqs/ctlseqs.html).

## Observed raw sequences

The parser behavior below is confirmed by automated unit tests. The available
tool PTY successfully started the WSL diagnostic and showed that `1002` and
`1006` were emitted, but it did not deliver injected input bytes to the raw
reader. Consequently these are parser-confirmed sequences, not physical mouse
observations from Windows Terminal.

### Keyboard

Existing raw parser tests confirm printable UTF-8, Ctrl letters, standalone Esc,
arrows, navigation keys, function keys, Alt prefixes, and CSI modifiers. The
combined diagnostic formats non-mouse packets as raw bytes, printable text, and
the parsed `ConsoleKeyInfo`.

Physical keyboard input in the new mode: **not yet confirmed**.

### Mouse click

Confirmed parser examples:

```text
ESC[<0;10;5M  -> Down Left   x=9 y=4
ESC[<1;10;5M  -> Down Middle x=9 y=4
ESC[<2;10;5M  -> Down Right  x=9 y=4
```

Physical clicks: **not yet confirmed**.

### Mouse release

`ESC[<0;10;5m` parses as an Up event at `x=9 y=4`. The parser preserves the
last pressed button so that the diagnostic can report the released button while
also showing the packet's original `cb` and final `m`.

Physical release packets: **not yet confirmed**.

### Mouse wheel

Confirmed parser examples:

```text
ESC[<64;10;5M -> Wheel WheelUp   x=9 y=4
ESC[<65;10;5M -> Wheel WheelDown x=9 y=4
```

Physical wheel events: **not yet confirmed**.

### Mouse drag

`ESC[<32;10;5M` parses as `Move Left x=9 y=4`. Mode `1002` is expected to
produce this class of packet only while a button is held, avoiding the noise of
all-motion mode `1003`.

Physical drag events: **not yet confirmed**.

### Modifiers

Parser tests confirm `Cb` bits 4, 8, and 16 as Shift, Alt, and Control. Combined
`Cb=28` reports all three modifiers.

Physical Shift/Ctrl/Alt click behavior is terminal- and desktop-dependent and
is **not yet confirmed**. In particular, terminal selection shortcuts may
consume Shift-modified mouse input before the application receives it.

### Resize

The diagnostic polls `driver.GetSize()` every 100 ms while waiting for raw
input and emits a resize record when width or height changes. This avoids using
the current non-raw `TryReadInput` path.

Interactive resize: **not yet confirmed** in the automated PTY.

## WSL / Windows Terminal notes

The WSL build succeeds with no warnings when Linux build artifacts are isolated
from Windows `obj` files. Sharing restored `obj/project.assets.json` across
Windows and WSL can fail because Windows NuGet fallback paths are not valid in
Linux; this is a build-environment issue, not an input-protocol result.

The Codex PTY reproduced the earlier raw-input limitation documented in spike
0057: the diagnostic entered raw mode and printed its header, but synthetic
bytes written through that PTY did not reach the raw reader. This does not prove
that direct Windows Terminal input fails. It means the PTY chain cannot be used
as evidence for physical click, wheel, drag, modifier, resize, `tmux`, or SSH
behavior.

Manual verification still required in a direct Windows Terminal WSL tab:

1. `abc`, Enter, Tab, Backspace, arrows, Ctrl+C, and Esc.
2. Left, right, and middle press/release; double-click.
3. Wheel up/down and left-button drag.
4. Shift/Ctrl/Alt click.
5. Window resize.
6. Repeat inside `tmux`.
7. Repeat over SSH when a representative target is available.
8. Confirm after every exit path that normal shell clicks do not emit input.

## Problems found

1. The available automated WSL PTY cannot validate real terminal mouse input.
2. `AnsiInputParser` remains a keyboard parser; the diagnostic must interpret
   `AnsiInputReadResult.Bytes` before trusting its semantic key result.
3. Release packets need pressed-button state for useful reporting across
   terminal variants.
4. Forced process termination cannot execute `finally`; production mouse mode
   would need lifecycle handling beyond this diagnostic's normal/exception
   restoration guarantees.
5. Shift/Ctrl/Alt mouse events may be intercepted by the terminal frontend or
   window manager and require physical testing.
6. The raw backend's behavior across Windows Terminal, `tmux`, SSH, native
   Linux terminals, and macOS remains unproven.

## Recommended production design

Current evidence is **not sufficient to safely move production Unix input to a
raw backend**. Unit tests establish parser semantics, and the diagnostic
establishes a controlled place to collect real evidence, but the target WSL
frontend scenarios have not yet produced physical observations.

If direct terminal testing confirms the expected packets, the likely design is:

1. Parse SGR mouse packets before keyboard CSI packets in a shared raw packet
   pipeline.
2. Prefer SGR mode `1006` over legacy X10 encoding.
3. Use `1002` for clicks, wheel, and button drag; do not enable noisy `1003` by
   default.
4. Scope mouse reporting to the application screen and disable it during
   restore, disposal, and child-process console mode.
5. Keep resize detection independent from blocking keyboard parsing.
6. Retain `Console.ReadKey` as the production baseline until raw keyboard and
   mouse behavior are both demonstrated across the supported terminal chains.

## Open questions

- Do direct Windows Terminal and Windows Terminal Preview sessions emit the
  tested SGR packets consistently in WSL?
- Which modified clicks are delivered rather than consumed by terminal UI?
- Does `tmux` preserve `1002/1006`, modifiers, release button identity, and
  coordinates without configuration changes?
- How should raw input cancellation and process termination restore mouse mode?
- Should future production input use one raw packetizer for keyboard, mouse,
  bracketed paste, and terminal replies?
- What fallback behavior is acceptable for terminals that do not support SGR
  mouse reporting?
