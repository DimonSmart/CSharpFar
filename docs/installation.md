# Installation

CSharpFar runs on Windows, Linux, and macOS. Packaged release builds are self-contained, so installing the .NET runtime is not required.

## macOS / Homebrew

Homebrew remains the package and verification mechanism for the macOS GUI distribution. The recommended installer is a small orchestration layer around the existing Cask.

### Recommended application install or update

With Homebrew already installed, run:

```bash
curl -fsSL https://dimonsmart.github.io/CSharpFar/install-macos.sh | bash
```

The same command is used for first installation and later updates. It:

- checks that it is running on macOS;
- finds Homebrew from `PATH`, `/opt/homebrew/bin/brew`, or `/usr/local/bin/brew`;
- adds `dimonsmart/csharpfar` when the tap is missing;
- runs `brew update`;
- installs or upgrades only `dimonsmart/csharpfar/csharpfar-app` in `/Applications`;
- refuses to overwrite an existing `/Applications/CSharpFar.app` that is not managed by Homebrew;
- removes `com.apple.quarantine` only from CSharpFar;
- verifies the installed `Contents/Resources/csharpfar --version`.

The installer never installs Homebrew automatically and never runs an unscoped `brew upgrade`.

The quarantine removal is a temporary workaround for the current unsigned and non-notarized application. It must be removed when Developer ID signing and notarization are introduced.

### Manual Homebrew application install

CSharpFar uses the main repository itself as a custom Homebrew tap:

```bash
brew tap dimonsmart/csharpfar https://github.com/DimonSmart/CSharpFar.git
brew install --cask dimonsmart/csharpfar/csharpfar-app
```

Homebrew selects the matching self-contained application archive automatically:

- Apple Silicon (`arm64`) uses `CSharpFar-v<version>-osx-arm64-app.zip`;
- Intel (`x86_64`) uses `CSharpFar-v<version>-osx-x64-app.zip`.

The Cask installs `CSharpFar.app` into Applications. Opening the app starts CSharpFar in the terminal selected under **Options → Settings → Application → Preferred terminal**. The default is Automatic (Kitty if installed, otherwise Terminal.app).

A direct manual Cask installation does not automatically clear quarantine. Because the application is currently unsigned and not notarized, recent macOS versions may require the standard Gatekeeper workflow:

1. Try to open `CSharpFar.app`, then dismiss the warning.
2. Open **System Settings → Privacy & Security**.
3. Use **Open Anyway** for CSharpFar and authenticate if requested.
4. Confirm the launch.

Manual update:

```bash
brew update
brew upgrade --cask dimonsmart/csharpfar/csharpfar-app
```

Uninstall:

```bash
brew uninstall --cask dimonsmart/csharpfar/csharpfar-app
```

### In-app update

For the standard Homebrew-managed application installed at:

```text
/Applications/CSharpFar.app
```

**About → Update now** is shown when a newer GitHub release exists and the currently running process is that exact Cask application. The update flow uses Homebrew rather than downloading or replacing the app itself:

1. `brew update`;
2. read the Cask version with `brew info --cask --json=v2`;
3. wait rather than upgrade if Homebrew metadata still trails the GitHub release;
4. `brew upgrade --cask --no-quit --appdir=/Applications csharpfar-app`;
5. verify the installed binary version;
6. clear and verify CSharpFar's quarantine attribute;
7. relaunch `/Applications/CSharpFar.app` and exit the old process.

For a manual copy, CLI Formula, development build, different application copy, or custom Homebrew `--appdir`, About keeps **Open release page** and does not offer self-update.

### Command-line installation

Install the CLI Formula when only the terminal command is needed:

```bash
brew install dimonsmart/csharpfar/csharpfar
```

The Formula selects the matching self-contained release automatically:

- Apple Silicon (`arm64`) uses the `osx-arm64` CLI release;
- Intel (`x86_64`) uses the `osx-x64` CLI release.

Then run:

```bash
csharpfar
```

Verify the installation:

```bash
csharpfar --version
csharpfar --self-test
```

Upgrade with:

```bash
brew update
brew upgrade dimonsmart/csharpfar/csharpfar
```

Uninstall with:

```bash
brew uninstall dimonsmart/csharpfar/csharpfar
```

The Formula and Cask can coexist: the Formula provides the `csharpfar` command while the Cask provides `CSharpFar.app` for Finder/Applications.

Both Homebrew packages are generated from SHA-256 checksums of the matching GitHub Release assets. After a successful Release workflow, the Homebrew workflow refreshes `Formula/csharpfar.rb` and `Casks/csharpfar-app.rb` on `master` automatically.

Because the repository name does not use Homebrew's `homebrew-<tap>` naming convention, the first manual `brew tap` command includes the explicit Git URL.

### CLI command PATH and Unix shell options

CSharpFar may be launched from Finder, Dock, Kitty, or Terminal.app with a
reduced `PATH`. On macOS, user-launched shell commands, registered
PowerShell aliases (`ps:` / `pwsh:`), and executable files launched from
panels receive a consistent `PATH`. Existing entries retain their order;
only existing directories are appended, in this order:
`/opt/homebrew/bin`, `/usr/local/bin`, `/opt/homebrew/sbin`,
`/usr/local/sbin`. Missing `PATH` uses
`/usr/bin:/bin:/usr/sbin:/sbin` before appending. The application process
environment, system updaters, and file associations are not changed.

The default `shell.macOsPathMode` is `appendStandard`, including for older
settings files that lack this field. To opt out, edit `settings.json` and set
`"macOsPathMode": "inherited"` under `"shell"`. Changes take effect after
restarting CSharpFar. An unrecognized mode triggers a warning and behaves as
`inherited`, without rewriting the file.

On macOS and Linux, `shell.argumentsFormat` specifies Unix shell flags
rather than a command-line template. Supported examples are `-c`, `-lc`,
`-lic`, `-l -c`, `-c {0}`, and `-l -c {0}`. The optional `{0}` must
be the last separate token; the user command is passed as **one argument**
after the flags, never interpolated into the format. Only short alphabetic
options are accepted, with a single final `c`. Invalid options fall back
to `-c` with a warning. The default remains `/bin/sh` with `-c`.

For example, to request a zsh login shell, configure:

```json
"shell": {
  "executable": "/bin/zsh",
  "argumentsFormat": "-lc",
  "macOsPathMode": "appendStandard"
}
```

A login shell does not necessarily read interactive startup files:
`zsh -lc` reads `.zprofile`, but not normally `.zshrc`. Use `-lic`
only intentionally: startup scripts can expect an interactive terminal.
The automatic macOS PATH policy does not initialize tools from custom
version managers (nvm, mise, pyenv, etc.) or support shell aliases/functions.
Windows `cmd.exe` handling is unchanged.

### Choose the preferred macOS terminal

In CSharpFar, open **Options → Settings → Application → Preferred terminal**.
Choose one of:

- **Automatic (recommended):** opens Kitty when a valid Kitty app is installed;
  otherwise opens Terminal.app.
- **Kitty:** prefers Kitty; if it is unavailable, falls back to Terminal.app
  without changing the saved preference.
- **Terminal.app:** always opens Apple's built-in Terminal.

Install Kitty separately from [the official Kitty site](https://sw.kovidgoyal.net/kitty/)
or with `brew install --cask kitty`. The launcher searches only
`/Applications/kitty.app` and `~/Applications/kitty.app`; it validates the
bundle identity and executable permissions. Kitty installed in a custom
location is not currently discovered.

Save and close CSharpFar, then open `CSharpFar.app` from Finder, Dock or
Spotlight. The preference is read from the existing `settings.json` on the
next bundle launch. Saving does **not** move a running session between
terminals. A malformed or unreadable settings file makes the launcher
conservatively use Terminal.app, without rewriting the file. Normal UI startup
may still reject other malformed settings.

A direct `csharpfar` command inside Kitty, Terminal.app, SSH, an IDE terminal,
or another interactive PTY remains in that terminal. Only opening the app
bundle **without** a TTY or command-line arguments invokes provider selection.
The command-line Formula and portable archives are unaffected.

### Terminal diagnostics for the macOS app

The executable inside `CSharpFar.app` can be launched directly from a real
terminal. This is the correct way to run terminal diagnostics because it keeps
stdin/stdout attached to the terminal emulator's TTY:

```bash
/Applications/CSharpFar.app/Contents/Resources/csharpfar --check-terminal
```

To inspect keyboard bytes and verify enhanced keyboard handling, run:

```bash
/Applications/CSharpFar.app/Contents/Resources/csharpfar --check-terminal --input-lab
```

The Input Lab uses the same production keyboard profile as CSharpFar and, when
the terminal supports the Kitty keyboard protocol, reports the requested and
confirmed flags together with Enter, Shift+Enter, Ctrl+Enter, and
Ctrl+Shift+Enter observations.

To diagnose macOS Git / Command Line Tools architecture mismatches, run:

```bash
/Applications/CSharpFar.app/Contents/Resources/csharpfar --check-system-git
```

This prints the CSharpFar process architecture, then launches Apple's
`/usr/bin/git --version` through the same Unix shell command builder used
for typed CSharpFar commands. A nonzero exit status means the launch failed.
This deliberately tests the macOS Git shim rather than any Homebrew Git.


### Kitty terminal keyboard support on macOS

To get reliable standalone Shift/Control/Alt press and release events, select
Kitty in the app's **Preferred terminal** setting and start it from Finder,
or run the CLI executable from a Kitty terminal. For a Homebrew CLI installation:

```bash
kitty -- csharpfar
```

Or run the app bundle's executable inside Kitty:

```bash
kitty -- /Applications/CSharpFar.app/Contents/Resources/csharpfar
```

CSharpFar negotiates Kitty keyboard flags **27**, including key press/repeat/
release reporting, and checks the terminal's confirmed response. If a terminal
only supports flags **25**, extended key chords continue to work, but standalone
modifier lifecycle is not available. Legacy VT terminals have their usual
keyboard limitations, including potentially indistinguishable Enter variants.

For Option to behave as Alt, put the following in `kitty.conf`:

```conf
macos_option_as_alt left
```

Use `macos_option_as_alt both` if both Option keys should act as Alt.
This changes how Option types special characters. Function keys may require
Fn/Globe on Mac keyboards, and some shortcuts are intercepted by macOS or
Kitty itself. CSharpFar does not alter Kitty configuration or require macOS
Accessibility permissions.

Verify the negotiated flags with `csharpfar --check-terminal`, and inspect
raw modifier press and release events with `csharpfar --check-terminal
--input-lab`. Inspect the bottom function-key row in the running application:
pressing and releasing each modifier should change and restore it without
pressing any other key.

## Releases

Published releases provide:

- a self-contained `win-x64` ZIP archive;
- a self-contained `linux-x64` tar.gz archive;
- a self-contained `osx-arm64` CLI tar.gz archive for Apple Silicon;
- a self-contained `osx-x64` CLI tar.gz archive for Intel Macs;
- an unsigned `CSharpFar.app` ZIP archive for Apple Silicon;
- an unsigned `CSharpFar.app` ZIP archive for Intel Macs;
- a Debian package for Linux.

The macOS app bundles contain the same self-contained `csharpfar` executable as the CLI release plus macOS bundle metadata, application icon, and a launcher that opens the terminal UI in Kitty (when available and preferred) or Terminal.app from Finder.

See the [GitHub Releases](https://github.com/DimonSmart/CSharpFar/releases) page for available versions and checksums.

## Ubuntu / Debian

The simplest installation method is the CSharpFar APT repository:

```bash
curl -fsSL https://dimonsmart.github.io/CSharpFar/install.sh | sh
```

Then run:

```bash
csharpfar
```

Verify the installation:

```bash
csharpfar --version
csharpfar --self-test
```

The published APT package currently targets amd64.

### Manual APT setup

```bash
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://dimonsmart.github.io/CSharpFar/apt/csharpfar-archive-keyring.gpg | sudo tee /etc/apt/keyrings/csharpfar.gpg > /dev/null
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/csharpfar.gpg] https://dimonsmart.github.io/CSharpFar/apt stable main" | sudo tee /etc/apt/sources.list.d/csharpfar.list > /dev/null
sudo apt update
sudo apt install csharpfar
```

## Build from source

Requirements:

- .NET 10 SDK;
- a supported Windows, Linux, or macOS terminal.

From the repository root:

```bash
dotnet restore CSharpFar.slnx
dotnet build CSharpFar.slnx
```

Run the Windows host:

```bash
dotnet run --project src/CSharpFar.Host.Windows/CSharpFar.Host.Windows.csproj
```

Run the Linux host:

```bash
dotnet run --project src/CSharpFar.Host.Linux/CSharpFar.Host.Linux.csproj
```

Run the macOS host:

```bash
dotnet run --project src/CSharpFar.Host.MacOs/CSharpFar.Host.MacOs.csproj
```
