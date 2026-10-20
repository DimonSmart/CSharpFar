param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [string]$IconSourcePath = "src/CSharpFar.App/Assets/CSharpFar.ico"
)

$ErrorActionPreference = 'Stop'

if (-not $IsMacOS) {
    throw 'The macOS application bundle must be assembled on macOS because iconutil and sips are required.'
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use MAJOR.MINOR.PATCH format. Got '$Version'."
}

function Export-LargestPngFromIco {
    param(
        [Parameter(Mandatory = $true)]
        [string]$IcoPath,
        [Parameter(Mandatory = $true)]
        [string]$PngPath
    )

    $bytes = [System.IO.File]::ReadAllBytes($IcoPath)
    if ($bytes.Length -lt 6 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0 -or [BitConverter]::ToUInt16($bytes, 2) -ne 1) {
        throw "Invalid ICO file: $IcoPath"
    }

    $count = [BitConverter]::ToUInt16($bytes, 4)
    $best = $null
    for ($index = 0; $index -lt $count; $index++) {
        $entryOffset = 6 + (16 * $index)
        if ($entryOffset + 16 -gt $bytes.Length) {
            throw "Invalid ICO directory entry in $IcoPath"
        }

        $width = if ($bytes[$entryOffset] -eq 0) { 256 } else { [int]$bytes[$entryOffset] }
        $height = if ($bytes[$entryOffset + 1] -eq 0) { 256 } else { [int]$bytes[$entryOffset + 1] }
        $length = [BitConverter]::ToUInt32($bytes, $entryOffset + 8)
        $offset = [BitConverter]::ToUInt32($bytes, $entryOffset + 12)
        if ($offset + $length -gt $bytes.Length -or $length -lt 8) {
            continue
        }

        $isPng = $bytes[$offset] -eq 0x89 -and
            $bytes[$offset + 1] -eq 0x50 -and
            $bytes[$offset + 2] -eq 0x4e -and
            $bytes[$offset + 3] -eq 0x47 -and
            $bytes[$offset + 4] -eq 0x0d -and
            $bytes[$offset + 5] -eq 0x0a -and
            $bytes[$offset + 6] -eq 0x1a -and
            $bytes[$offset + 7] -eq 0x0a

        if (-not $isPng) {
            continue
        }

        $area = $width * $height
        if ($null -eq $best -or $area -gt $best.Area) {
            $best = [pscustomobject]@{ Area = $area; Offset = [int]$offset; Length = [int]$length }
        }
    }

    if ($null -eq $best) {
        throw "ICO file does not contain a PNG-compressed image: $IcoPath"
    }

    $pngBytes = New-Object byte[] $best.Length
    [Array]::Copy($bytes, $best.Offset, $pngBytes, 0, $best.Length)
    [System.IO.File]::WriteAllBytes($PngPath, $pngBytes)
}

$publishDirPath = (Resolve-Path -LiteralPath $PublishDir).Path
$iconSource = (Resolve-Path -LiteralPath $IconSourcePath).Path
$binarySource = Join-Path $publishDirPath 'csharpfar'

if (-not (Test-Path -LiteralPath $binarySource -PathType Leaf)) {
    throw "Published macOS binary not found: $binarySource"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$outputDirPath = (Resolve-Path -LiteralPath $OutputDir).Path
$appDir = Join-Path $outputDirPath 'CSharpFar.app'
$contentsDir = Join-Path $appDir 'Contents'
$macOsDir = Join-Path $contentsDir 'MacOS'
$resourcesDir = Join-Path $contentsDir 'Resources'
$iconSetDir = Join-Path $outputDirPath 'CSharpFar.iconset'
$iconPngPath = Join-Path $outputDirPath 'CSharpFar.icon-source.png'

Remove-Item -LiteralPath $appDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $iconSetDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $iconPngPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $macOsDir, $resourcesDir, $iconSetDir | Out-Null

$binaryTarget = Join-Path $resourcesDir 'csharpfar'
Copy-Item -LiteralPath $binarySource -Destination $binaryTarget
chmod +x $binaryTarget

# Build the native AppKit/PTY host on the target macOS architecture. The
# portable CLI remains byte-for-byte unchanged in Contents/Resources.
$hostPackage = Join-Path $PSScriptRoot '..' '..' 'eng' 'macos-host'
$hostPackage = (Resolve-Path -LiteralPath $hostPackage).Path
# Build output is diagnostic; keep only the final bundle path on the success pipeline.
& swift build --package-path $hostPackage --configuration release | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw 'Building the Swift desktop terminal host failed.'
}
$hostBuildDir = Join-Path $hostPackage '.build' 'release'
$hostSource = Join-Path $hostBuildDir 'CSharpFarHost'
if (-not (Test-Path -LiteralPath $hostSource -PathType Leaf)) {
    throw "Swift desktop host was not produced: $hostSource"
}
$hostTarget = Join-Path $macOsDir 'CSharpFarHost'
Copy-Item -LiteralPath $hostSource -Destination $hostTarget
chmod +x $hostTarget

# SwiftPM resource bundles (including SwiftTerm's optional Metal shaders)
# must accompany a standalone Swift executable after it leaves .build.
Get-ChildItem -LiteralPath $hostBuildDir -Filter '*.bundle' -Directory -Recurse |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $resourcesDir -Recurse -Force }

$launcher = @'
#!/bin/zsh
set -euo pipefail
script_dir="$(cd "$(dirname "$0")" && pwd)"
resources_dir="$(cd "$script_dir/../Resources" && pwd)"
binary="$resources_dir/csharpfar"

# Real terminal launches and diagnostics stay CLI-only. Interactive Finder
# launches pass all application arguments to the embedded terminal session.
if [[ -t 0 && -t 1 ]]; then
    exec "$binary" "$@"
fi
if (( $# > 0 )); then
    case "$1" in
        --version|--self-test|--check-system-git|--check-terminal)
            exec "$binary" "$@"
            ;;
    esac
fi
exec "$script_dir/CSharpFarHost" "$@"
'@

$launcherPath = Join-Path $macOsDir 'CSharpFar'
Set-Content -LiteralPath $launcherPath -Value $launcher -Encoding utf8NoBOM
chmod +x $launcherPath

Export-LargestPngFromIco -IcoPath $iconSource -PngPath $iconPngPath
$iconSizes = @(
    @{ Name = 'icon_16x16.png'; Size = 16 },
    @{ Name = 'icon_16x16@2x.png'; Size = 32 },
    @{ Name = 'icon_32x32.png'; Size = 32 },
    @{ Name = 'icon_32x32@2x.png'; Size = 64 },
    @{ Name = 'icon_128x128.png'; Size = 128 },
    @{ Name = 'icon_128x128@2x.png'; Size = 256 },
    @{ Name = 'icon_256x256.png'; Size = 256 },
    @{ Name = 'icon_256x256@2x.png'; Size = 512 },
    @{ Name = 'icon_512x512.png'; Size = 512 },
    @{ Name = 'icon_512x512@2x.png'; Size = 1024 }
)

foreach ($icon in $iconSizes) {
    $target = Join-Path $iconSetDir $icon.Name
    & /usr/bin/sips -z $icon.Size $icon.Size $iconPngPath --out $target | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sips failed while generating $($icon.Name)."
    }
}

$icnsPath = Join-Path $resourcesDir 'CSharpFar.icns'
& /usr/bin/iconutil -c icns $iconSetDir -o $icnsPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $icnsPath -PathType Leaf)) {
    throw 'iconutil failed to create CSharpFar.icns.'
}
Remove-Item -LiteralPath $iconSetDir -Recurse -Force
Remove-Item -LiteralPath $iconPngPath -Force

$infoPlist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleDisplayName</key>
    <string>CSharpFar</string>
    <key>CFBundleExecutable</key>
    <string>CSharpFar</string>
    <key>CFBundleIconFile</key>
    <string>CSharpFar</string>
    <key>CFBundleIdentifier</key>
    <string>com.dimonsmart.csharpfar</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>CSharpFar</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$Version</string>
    <key>CFBundleVersion</key>
    <string>$Version</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
"@

$infoPlistPath = Join-Path $contentsDir 'Info.plist'
Set-Content -LiteralPath $infoPlistPath -Value $infoPlist -Encoding utf8NoBOM
& /usr/bin/plutil -lint $infoPlistPath | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw 'Generated Info.plist is invalid.'
}

# Never ship a host of the wrong architecture or with non-system dylib dependencies.
$expectedArch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'arm64' } else { 'x86_64' }
$hostArch = (& /usr/bin/lipo -archs $hostTarget).Trim()
if ($LASTEXITCODE -ne 0 -or $hostArch -ne $expectedArch) {
    throw "Desktop host architecture '$hostArch' does not match '$expectedArch'."
}
$linkedLibraries = & /usr/bin/otool -L $hostTarget
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect desktop host dependencies.' }
foreach ($line in ($linkedLibraries | Select-Object -Skip 1)) {
    $library = ($line.Trim() -split '\\s+')[0]
    if ($library -and
        -not $library.StartsWith('/usr/lib/') -and
        -not $library.StartsWith('/System/Library/') -and
        -not $library.StartsWith('@rpath/')) {
        throw "Desktop host has an unbundled dynamic library: $library"
    }
}

$versionOutput = & $launcherPath --version
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch [regex]::Escape($Version)) {
    throw "Application bundle launcher version check failed. Output: $versionOutput"
}

Write-Output $appDir
