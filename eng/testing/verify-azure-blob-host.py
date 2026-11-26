#!/usr/bin/env python3
"""Exercise the actual published Linux executable with an installed extension."""
import fcntl
import json
import os
import pathlib
import pty
import select
import shutil
import signal
import struct
import sys
import tempfile
import termios
import time

import pyte
from PIL import Image, ImageDraw, ImageFont


def check(host, extension, screenshot):
    host = host.resolve(strict=True)
    extension = extension.resolve(strict=True)
    screenshot = screenshot.resolve()
    with tempfile.TemporaryDirectory(prefix="azure-blob-host-") as work:
        work = pathlib.Path(work)
        (host.parent / "CSharpFar.portable").touch()
        config = host.parent / "CSharpFar.config"
        if config.exists():
            raise AssertionError("Smoke test requires an unused, isolated published host.")
        shutil.copytree(extension, config / "extensions" / "azure-blob")
        settings = config / "plugins" / "azure-blob"
        settings.mkdir(parents=True)
        (settings / "connections.json").write_text(json.dumps({
            "Version": 1,
            "Connections": [{
                "Id": "d33fe4f1-2986-4ec4-80a6-2a33550be088",
                "DisplayName": "AzureSmoke",
                "AuthenticationMode": 4,
                "AccountEndpoint": None,
                "ContainerName": "csharpfar-smoke",
                "InitialDirectory": "/",
                "CredentialReference": None,
                "ReadOnly": False
            }]
        }))
        (work / "local-upload.txt").write_text("LOCAL_AZURE_PROBE")
        screen = pyte.Screen(120, 35)
        stream = pyte.ByteStream(screen)
        pid, terminal = pty.fork()
        if pid == 0:
            os.environ.update(HOME=str(work), XDG_CONFIG_HOME=str(work), TERM="xterm-256color")
            os.chdir(work)
            os.execv(str(host), [str(host)])
        output = bytearray()

        def pump():
            if not select.select([terminal], [], [], 0.1)[0]:
                return
            chunk = os.read(terminal, 65536)
            if not chunk:
                raise AssertionError("CSharpFar exited unexpectedly")
            if b"\x1b[6n" in bytes(output[-3:]) + chunk:
                os.write(terminal, b"\x1b[1;1R")
            output.extend(chunk)
            stream.feed(chunk)

        def visible():
            return "\n".join(screen.display)

        def wait_for(predicate, label):
            deadline = time.monotonic() + 25
            while not predicate():
                if time.monotonic() >= deadline:
                    raise AssertionError(f"Timed out waiting for {label}:\n{visible()}")
                pump()

        def wait_text(text):
            wait_for(lambda: text in visible(), text)

        def send(data):
            os.write(terminal, data)

        def capture():
            # Render the actual VT screen cells; not a fabricated UI image.
            screenshot.parent.mkdir(parents=True, exist_ok=True)
            colors = {"black":"#000000", "red":"#aa0000", "green":"#00aa00",
                      "brown":"#aa5500", "yellow":"#aaaa00", "blue":"#0000aa",
                      "magenta":"#aa00aa", "cyan":"#00aaaa", "white":"#aaaaaa",
                      "default":"#e0e0e0"}
            font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", 15)
            image = Image.new("RGB", (120 * 9, 35 * 19), "#000000")
            draw = ImageDraw.Draw(image)
            def color(name, default):
                if name == "default":
                    return default
                return colors.get(name, "#" + name if len(name) == 6 else default)
            for y in range(35):
                for x in range(120):
                    cell = screen.buffer[y][x]
                    foreground, background = color(cell.fg, "#e0e0e0"), color(cell.bg, "#000000")
                    if cell.reverse:
                        foreground, background = background, foreground
                    draw.rectangle((x*9,y*19,x*9+8,y*19+18), fill=background)
                    draw.text((x*9,y*19), cell.data, font=font, fill=foreground)
            image.save(screenshot)

        try:
            fcntl.ioctl(terminal, termios.TIOCSWINSZ, struct.pack("HHHH", 35, 120, 0, 0))
            wait_for(lambda: b"\x1b[?1003h" in output, "terminal initialization")
            wait_text("Quit")
            send(b"azure:\r")
            wait_text("Quick connect")
            send(b"\r")
            wait_text("Edit connection")
            send(b"\r")
            wait_text("probe-view.txt")
            wait_text("folder")
            capture()
            send(b"\x1b[F")  # End: last file in the remote panel
            send(b"\x1bOR")   # F3
            wait_text("AZURE_PROBE_VIEW")
            send(b"\x1b")
            wait_text("probe-view.txt")
            print("PASS: installed DLL, public command, connection selection, remote panel and F3 viewer.")
            send(b"\x1b[21~")  # F10
            # Quit confirmation defaults to Cancel.
            deadline = time.monotonic() + 15
            while time.monotonic() < deadline:
                result, status = os.waitpid(pid, os.WNOHANG)
                if result:
                    if status != 0:
                        raise AssertionError(f"CSharpFar exit status {status}")
                    pid = None
                    break
                if "Exit CSharpFar" in visible():
                    send(b"\x1b[D\r")
                    # Don't keep sending keys while the process is shutting down.
                    time.sleep(0.2)
                try:
                    pump()
                except OSError:
                    time.sleep(0.1)
            if pid is not None:
                raise AssertionError("CSharpFar did not exit after F10")
        finally:
            if pid is not None:
                try:
                    os.killpg(pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                os.waitpid(pid, 0)
            os.close(terminal)


if __name__ == "__main__":
    check(*(pathlib.Path(value) for value in sys.argv[1:]))
