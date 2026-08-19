param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ChecksumsPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use MAJOR.MINOR.PATCH format. Got '$Version'."
}

if (-not (Test-Path -LiteralPath $ChecksumsPath -PathType Leaf)) {
    throw "Checksums file not found: $ChecksumsPath"
}

$tag = "v$Version"
$armAsset = "CSharpFar-$tag-osx-arm64-app.zip"
$x64Asset = "CSharpFar-$tag-osx-x64-app.zip"
$checksums = @{}

foreach ($line in Get-Content -LiteralPath $ChecksumsPath) {
    if ($line -match '^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') {
        $checksums[$Matches[2]] = $Matches[1].ToLowerInvariant()
    }
}

foreach ($asset in @($armAsset, $x64Asset)) {
    if (-not $checksums.ContainsKey($asset)) {
        throw "Checksum for '$asset' was not found in $ChecksumsPath."
    }
}

$cask = @'
cask "csharpfar-app" do
  arch arm: "arm64", intel: "x64"

  version "__VERSION__"
  sha256 arm: "__ARM64_SHA256__",
         intel: "__X64_SHA256__"

  url "https://github.com/DimonSmart/CSharpFar/releases/download/v#{version}/CSharpFar-v#{version}-osx-#{arch}-app.zip"
  name "CSharpFar"
  desc "Far-inspired terminal file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"

  app "CSharpFar.app"

  caveats <<~EOS
    CSharpFar is a terminal application. Opening CSharpFar.app launches it in Terminal.

    The macOS application is currently unsigned and not notarized. On first launch,
    macOS may require using Open from the Finder context menu.

    For a command-only installation use:
      brew install dimonsmart/csharpfar/csharpfar
  EOS
end
'@

$cask = $cask.Replace('__VERSION__', $Version)
$cask = $cask.Replace('__ARM64_SHA256__', $checksums[$armAsset])
$cask = $cask.Replace('__X64_SHA256__', $checksums[$x64Asset])

$outputDirectory = Split-Path -Path $OutputPath -Parent
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $cask -Encoding utf8NoBOM
Write-Host "Generated Homebrew Cask for $tag at $OutputPath"
