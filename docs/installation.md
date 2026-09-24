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

The Cask installs `CSharpFar.app` into Applications. Opening the app launches CSharpFar in Terminal because the application itself is a terminal UI.

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
4. `brew upgrade --cask --no-quit --appdir=/Applications dimonsmart/csharpfar/csharpfar-app`;
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

## Releases

Published releases provide:

- a self-contained `win-x64` ZIP archive;
- a self-contained `linux-x64` tar.gz archive;
- a self-contained `osx-arm64` CLI tar.gz archive for Apple Silicon;
- a self-contained `osx-x64` CLI tar.gz archive for Intel Macs;
- an unsigned `CSharpFar.app` ZIP archive for Apple Silicon;
- an unsigned `CSharpFar.app` ZIP archive for Intel Macs;
- a Debian package for Linux.

The macOS app bundles contain the same self-contained `csharpfar` executable as the CLI release plus macOS bundle metadata, application icon, and a launcher that opens the terminal UI in Terminal when started from Finder.

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
