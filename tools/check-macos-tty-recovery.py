"""macOS PTY smoke test. Usage: python3 tools/check-macos-tty-recovery.py /path/to/csharpfar."""
import fcntl
import os
from pathlib import Path
import pty
import select
import shutil
import struct
import sys
import tempfile
import termios
import time

if sys.platform != 'darwin' or len(sys.argv) != 2:
    sys.exit(__doc__)

source = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory() as directory:
    fixture = Path(directory)
    app = fixture / 'app'
    shutil.copytree(source.parent, app)
    (app / 'CSharpFar.portable').touch()
    work = fixture / 'work'
    work.mkdir()
    binary = str(app / source.name)
    pid, fd = pty.fork()
    if pid == 0:
        os.environ['TERM'] = 'xterm-256color'
        os.chdir(work)
        os.execv(binary, [binary])
    try:
        fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack('HHHH', 35, 120, 0, 0))
        def until(check, label):
            deadline = time.monotonic() + 8
            output = b''
            responses = 0
            while time.monotonic() < deadline:
                if select.select([fd], [], [], .03)[0]:
                    chunk = os.read(fd, 65536)
                    output += chunk
                    while responses < output.count(b'\x1b[6n'):
                        os.write(fd, b'\x1b[35;1R')
                        responses += 1
                if check(output):
                    return
            raise AssertionError((label, output[-1200:]))
        def raw():
            return not termios.tcgetattr(fd)[3] & (termios.ICANON | termios.ECHO)
        until(lambda out: raw() and b'Quit' in out, 'startup')
        os.write(fd, b'echo PTY_CHILD; sleep 1\r')
        until(lambda out: not raw() and b'PTY_CHILD\r\n' in out, 'child cooked mode/output')
        until(lambda out: raw() and b'Quit' in out and b'\x1b[?1003h\x1b[?1006h' in out,
              'return to panels with mouse reporting')
        # Model a later writer to the shared TTY, after the panels are back.
        mode = termios.tcgetattr(fd)
        mode[3] |= termios.ICANON | termios.ECHO
        termios.tcsetattr(fd, termios.TCSANOW, mode)
        until(lambda out: raw(), 'late cooked mode must be repaired')
        os.write(fd, b'echo PTY_INPUT_OK\r')
        until(lambda out: b'PTY_INPUT_OK\r\n' in out, 'keyboard works after repair')
        until(lambda out: raw(), 'second return to raw mode')
        print('PASS: child cooked mode, raw restored after late reset, keyboard executes next command')
    finally:
        os.kill(pid, 9)
        os.waitpid(pid, 0)
        os.close(fd)
