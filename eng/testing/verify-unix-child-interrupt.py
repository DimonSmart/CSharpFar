#!/usr/bin/env python3
"""Run against a built Unix host: python3 verify-unix-child-interrupt.py <host.dll>.

Uses a real PTY and harmless commands; isolates settings in a temporary home.
"""

import fcntl
import os
from pathlib import Path
import pty
import select
import signal
import struct
import sys
import tempfile
import termios
import time


def verify(host):
    with tempfile.TemporaryDirectory(prefix="csharpfar-interrupt-") as home:
        pid, terminal = pty.fork()
        if pid == 0:
            os.environ.update(HOME=home, XDG_CONFIG_HOME=home, TERM="xterm-256color")
            os.chdir(home)
            os.execvp("dotnet", ["dotnet", str(host)])

        output = bytearray()

        def wait_for(marker):
            deadline = time.monotonic() + 15
            while marker not in output:
                if time.monotonic() >= deadline:
                    raise AssertionError(
                        f"Timed out waiting for {marker!r}; output tail: {bytes(output[-1500:])!r}"
                    )
                if select.select([terminal], [], [], 0.1)[0]:
                    try:
                        chunk = os.read(terminal, 65536)
                    except OSError as error:
                        raise AssertionError("CSharpFar exited before panels returned") from error
                    if not chunk:
                        raise AssertionError("CSharpFar closed its terminal")
                    # Answer System.Console's cursor-position query like a terminal.
                    if b"\x1b[6n" in bytes(output[-3:]) + chunk:
                        os.write(terminal, b"\x1b[1;1R")
                    output.extend(chunk)
            output.clear()

        try:
            fcntl.ioctl(terminal, termios.TIOCSWINSZ, struct.pack("HHHH", 30, 100, 0, 0))
            wait_for(b"\x1b[?1049h")
            # Repeated execution also checks that returning to application input
            # mode does not break the next command or its interrupt handling.
            for attempt in range(2):
                marker = f"CF_CHILD_READY_{attempt}".encode()
                command = f"/bin/sh -c 'printf \"%s%s\\n\" CF_CHILD_ READY_{attempt}; exec sleep 60'\r"
                os.write(terminal, command.encode())
                wait_for(marker)
                if not termios.tcgetattr(terminal)[3] & termios.ISIG:
                    raise AssertionError("Child command inherited raw mode; Ctrl+C cannot signal it")
                os.write(terminal, b"\x03")
                wait_for(b"\x1b[?1049h")
                os.kill(pid, 0)
            print("PASS: Ctrl+C interrupted both children and restored panels; CSharpFar survived.")
        finally:
            # Terminate any remaining child too if an assertion failed.
            try:
                os.killpg(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            os.close(terminal)
            os.waitpid(pid, 0)


if __name__ == "__main__":
    verify(Path(sys.argv[1]).resolve(strict=True))
