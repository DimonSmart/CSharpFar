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
$armAsset = "CSharpFar-$tag-osx-arm64.tar.gz"
$x64Asset = "CSharpFar-$tag-osx-x64.tar.gz"
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

$formula = @'
class Csharpfar < Formula
  desc "Cross-platform, Far-inspired file manager built with C# and .NET"
  homepage "https://github.com/DimonSmart/CSharpFar"
  version "__VERSION__"

  on_arm do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v__VERSION__/CSharpFar-v__VERSION__-osx-arm64.tar.gz"
    sha256 "__ARM64_SHA256__"
  end

  on_intel do
    url "https://github.com/DimonSmart/CSharpFar/releases/download/v__VERSION__/CSharpFar-v__VERSION__-osx-x64.tar.gz"
    sha256 "__X64_SHA256__"
  end

  depends_on :macos

  def install
    bin.install "csharpfar"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/csharpfar --version")
  end
end
'@

$formula = $formula.Replace('__VERSION__', $Version)
$formula = $formula.Replace('__ARM64_SHA256__', $checksums[$armAsset])
$formula = $formula.Replace('__X64_SHA256__', $checksums[$x64Asset])

$outputDirectory = Split-Path -Path $OutputPath -Parent
if ($outputDirectory) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

Set-Content -LiteralPath $OutputPath -Value $formula -Encoding utf8NoBOM
Write-Host "Generated Homebrew formula for $tag at $OutputPath"
