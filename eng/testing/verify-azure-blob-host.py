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
        (config / "settings.json").write_text(json.dumps({"application": {"confirmExit": False}, "ui": {"confirmDelete": False}}))
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
            wait_text("local-upload.txt")
            send(b"azure:\r")
            wait_text("Quick connect")
            send(b"\r")
            wait_text("Edit connection")
            send(b"\r")
            wait_text("probe-view.txt")
            wait_text("folder")
            capture()
            send(b"\x1b[H")
            wait_text("/folder/")
            send(b"\r")
            wait_text("nested.txt")
            send(b"\x1b[H\r")
            wait_text("probe-view.txt")
            send(b"\x1b[H")
            wait_text("/folder/")
            send(b"\x1b[15~")
            wait_text("Already existing files:")
            send(b"\r")
            wait_for(lambda: (work / "folder" / "nested.txt").exists() and
                     (work / "folder" / "nested.txt").read_text() == "NESTED_AZURE_PROBE", "downloaded nested directory")
            wait_for(lambda: "Already existing files:" not in visible() and "Quit" in visible(), "directory copy completed")
            send(b"\x1b[F")  # End: last file in the remote panel
            send(b"\x1bOR")   # F3
            wait_text("AZURE_PROBE_VIEW")
            send(b"\x1b")
            wait_for(lambda: "folder" in visible() and "Quit" in visible() and "AZURE_PROBE_VIEW" not in visible(), "return to file panels")
            print("PASS: installed DLL, public command, connection selection, remote panel and F3 viewer.")
            send(b"\x1b[15~")  # F5, download selected remote file
            wait_text("Copy probe-view.txt to:")
            send(b"\r")
            wait_for(lambda: (work / "probe-view.txt").exists() and
                     (work / "probe-view.txt").read_text() == "AZURE_PROBE_VIEW",
                     "downloaded file")
            wait_for(lambda: "Copy probe-view.txt to:" not in visible() and "Quit" in visible(), "copy completed")
            # At the remote root, create a new directory and remove that same directory.
            send(b"\x1b[18~")
            wait_text("Create the folder:")
            send(b"smoke-created\r")
            wait_for(lambda: "smoke-created" in visible() and "Create the folder:" not in visible(), "directory created")
            # Async directory refresh may preserve the previously selected file.
            # Select the newly created directory explicitly and wait for its committed item footer.
            send(b"\x1b[H")
            send(b"\x1b[B")
            wait_text("/smoke-created/")
            send(b"\x1b[19~")
            wait_for(lambda: "smoke-created" not in visible() and "Quit" in visible(), "directory deleted")
            print("PASS: standard F5 download, F7 create directory and F8 delete.")
            # Upload from the passive local panel using the normal copy dialog.
            send(b"\t")
            time.sleep(0.2)
            send(b"\x1b[F")
            send(b"\x1b[A")
            send(b"\x1b[15~")
            wait_text("Copy local-upload.txt to:")
            send(b"\r")
            wait_for(lambda: visible().count("local-upload.txt") >= 2 and "Copy local-upload.txt to:" not in visible(),
                     "uploaded file visible in both panels")
            print("PASS: standard F5 upload to Azure.", flush=True)
            # Allow the asynchronous file operation and refresh to release the input barrier.
            settle = time.monotonic() + 1
            while time.monotonic() < settle:
                pump()
            deadline = time.monotonic() + 20
            next_exit_attempt = 0
            exit_attempt = 0
            while time.monotonic() < deadline:
                result, status = os.waitpid(pid, os.WNOHANG)
                if result:
                    pid = None
                    if status != 0:
                        raise AssertionError(f"CSharpFar exit status {status}")
                    print("PASS: CSharpFar exited cleanly.", flush=True)
                    break
                if time.monotonic() >= next_exit_attempt:
                    next_exit_attempt = time.monotonic() + 1
                    if "Exit CSharpFar" in visible():
                        send(b"e")
                    elif "Quit" in screen.display[-1]:
                        if exit_attempt % 2 == 0:
                            send(b"\x1b[21~")  # F10
                        else:
                            x = screen.display[-1].index("Quit") + 2
                            send(f"\x1b[<0;{x};35M\x1b[<0;{x};35m".encode())
                        exit_attempt += 1
                try:
                    pump()
                except (OSError, AssertionError):
                    time.sleep(0.1)
            if pid is not None:
                raise AssertionError(f"CSharpFar did not exit after F10:\n{visible()}")
        finally:
            if pid is not None:
                try:
                    os.killpg(pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                os.waitpid(pid, 0)
            os.close(terminal)


def check_lifecycle(host, development=False):
    """Verify the actual published host's persisted enable/disable UI and restart."""
    host = host.resolve(strict=True)
    config = host.parent / "CSharpFar.config"
    profiles = config / "plugins" / "azure-blob" / "connections.json"
    original_profiles = json.loads(profiles.read_text())
    state = config / "extensions-state.json"

    def session(expected, toggle=None, no_extensions=False, global_toggle=None, all_enabled=None):
        with tempfile.TemporaryDirectory(prefix="azure-blob-lifecycle-") as work:
            screen = pyte.Screen(120, 35)
            stream = pyte.ByteStream(screen)
            pid, terminal = pty.fork()
            if pid == 0:
                os.environ.update(HOME=work, XDG_CONFIG_HOME=work, TERM="xterm-256color")
                os.chdir(work)
                os.execv(str(host), [str(host)] + (["--no-extensions"] if no_extensions else []))
            previous = b""

            def send(data):
                os.write(terminal, data)

            def pump():
                nonlocal previous
                if not select.select([terminal], [], [], 0.1)[0]:
                    return
                chunk = os.read(terminal, 65536)
                if not chunk:
                    raise AssertionError("CSharpFar exited unexpectedly")
                if b"\x1b[6n" in previous + chunk:
                    send(b"\x1b[1;1R")
                previous = chunk[-3:]
                stream.feed(chunk)

            def visible():
                return "\n".join(screen.display)

            def wait_for(predicate, description):
                deadline = time.monotonic() + 25
                while not predicate():
                    if time.monotonic() >= deadline:
                        raise AssertionError(f"Timed out waiting for {description}:\n{visible()}")
                    pump()

            def wait_text(value):
                wait_for(lambda: value in visible(), value)

            try:
                fcntl.ioctl(terminal, termios.TIOCSWINSZ, struct.pack("HHHH", 35, 120, 0, 0))
                wait_text("Quit")
                send(b"\x1b[20~")  # F9
                wait_text("Options")
                send(b"o")
                wait_text("Plugins...")
                send(b"p")
                wait_text("Enable/Disable")
                wait_for(lambda: any("Azure Blob Storage" in row and expected in row for row in screen.display),
                         "Azure extension status " + expected)
                if all_enabled is not None:
                    wait_text(("[X]" if all_enabled else "[ ]") + " Enable external plugins (all)")
                if global_toggle is not None:
                    send(b"\x1b[F")  # End selects the global row.
                    send(b"\r")
                    wait_text("are now " + ("enabled" if global_toggle else "disabled"))
                    send(b"\r")
                    wait_text("Enable/Disable")
                    assert json.loads(state.read_text())["enabled"] is global_toggle
                if toggle:
                    send(b"\r")
                    wait_text("is now " + toggle)
                    send(b"\r")
                    wait_text("Enable/Disable")
                    assert ("azure-blob" in json.loads(state.read_text())["disabled"]) == (toggle == "disabled")
                send(b"\x1b")
                wait_for(lambda: "Enable/Disable" not in visible() and "Quit" in visible(), "main panel")
                if expected == "Loaded" and toggle is None and global_toggle is None:
                    send(b"\x1b[20~")
                    wait_text("Plugins")
                    send(b"p")
                    wait_text("Azure Blob Storage")
                    send(b"\x1b")
                    wait_for(lambda: "Azure Blob Storage" not in visible(), "close plugin menu")
                    send(b"azure:\r")
                    wait_text("AzureSmoke")
                    send(b"\x1b")
                    wait_for(lambda: "Quick connect" not in visible(), "close connection manager")
                deadline = time.monotonic() + 20
                next_attempt = 0
                while time.monotonic() < deadline:
                    result, status = os.waitpid(pid, os.WNOHANG)
                    if result:
                        pid = None
                        if status != 0:
                            raise AssertionError(f"CSharpFar exit status {status}")
                        break
                    if time.monotonic() >= next_attempt:
                        next_attempt = time.monotonic() + 1
                        if "Exit CSharpFar" in visible():
                            send(b"e")
                        elif "Quit" in screen.display[-1]:
                            x = screen.display[-1].index("Quit") + 2
                            send(f"\x1b[<0;{x};35M\x1b[<0;{x};35m".encode())
                    try:
                        pump()
                    except (OSError, AssertionError):
                        time.sleep(0.1)
                if pid is not None:
                    raise AssertionError(f"CSharpFar did not exit during lifecycle test:\n{visible()}")
            finally:
                if pid is not None:
                    try:
                        os.killpg(pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
                    os.waitpid(pid, 0)
                os.close(terminal)

    session("Loaded", toggle="disabled")
    session("Disabled", toggle="enabled")
    session("Loaded")
    session("Disabled", no_extensions=True)
    if development:
        session("Loaded", global_toggle=False, all_enabled=True)
        session("Disabled", global_toggle=True, all_enabled=False)
        session("Loaded", all_enabled=True)
    assert json.loads(profiles.read_text()) == original_profiles
    assert "azure-blob" not in json.loads(state.read_text())["disabled"]
    print("PASS: actual Options > Plugins disable/re-enable with restarts, plugin menu, saved profile preservation, --no-extensions.", flush=True)


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "--debug-lifecycle":
        host = pathlib.Path(sys.argv[2]).resolve(strict=True)
        source_config = pathlib.Path(sys.argv[3])
        (host.parent / "CSharpFar.portable").touch()
        config = host.parent / "CSharpFar.config"
        if config.exists():
            raise AssertionError("Debug lifecycle test requires an unused host.")
        # Copy settings only, not the installed package: discovery must use the staged build output.
        shutil.copytree(source_config / "plugins", config / "plugins")
        shutil.copy2(source_config / "settings.json", config / "settings.json")
        check_lifecycle(host, development=True)
    else:
        check(*(pathlib.Path(value) for value in sys.argv[1:]))
        check_lifecycle(pathlib.Path(sys.argv[1]))
