# Installation

CSharpFar runs on Windows, Linux, and macOS. Packaged release builds are self-contained, so installing the .NET runtime is not required.

## macOS / Homebrew

Homebrew is the recommended installation method on macOS.

CSharpFar uses the main repository itself as a custom Homebrew tap. Add the tap once and install the formula:

```bash
brew tap dimonsmart/csharpfar https://github.com/DimonSmart/CSharpFar.git
brew install dimonsmart/csharpfar/csharpfar
```

The formula selects the matching self-contained release automatically:

- Apple Silicon (`arm64`) uses the `osx-arm64` release;
- Intel (`x86_64`) uses the `osx-x64` release.

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

The Homebrew formula is generated from the SHA-256 checksums of the matching GitHub Release assets. After a successful Release workflow, the Homebrew workflow updates `Formula/csharpfar.rb` on `master` automatically.

Because the repository name does not use Homebrew's `homebrew-<tap>` naming convention, the first `brew tap` command includes the explicit Git URL. Installing the fully qualified formula keeps the trust scope limited to that formula.

## Releases

Published releases provide:

- a self-contained `win-x64` ZIP archive;
- a self-contained `linux-x64` tar.gz archive;
- a self-contained `osx-arm64` tar.gz archive for Apple Silicon;
- a self-contained `osx-x64` tar.gz archive for Intel Macs;
- a Debian package for Linux.

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
