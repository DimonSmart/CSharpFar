# Installation

CSharpFar runs on Windows and Linux. Release builds are self-contained, so installing the .NET runtime is not required when using a packaged release.

## Releases

Published releases provide:

- a self-contained `win-x64` ZIP archive;
- a self-contained `linux-x64` tar.gz archive;
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
- a supported Windows or Linux terminal.

From the repository root:

```bash
dotnet restore CSharpFar.slnx
dotnet build CSharpFar.slnx
```

Run the Windows host:

```bash
dotnet run --project src/CSharpFar.Host.Windows/CSharpFar.Host.Windows.csproj
```

Run the Unix host:

```bash
dotnet run --project src/CSharpFar.Host.Linux/CSharpFar.Host.Linux.csproj
```
