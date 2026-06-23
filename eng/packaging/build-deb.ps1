[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDir,

    [Parameter(Mandatory = $true)]
    [string] $Version,

    [string] $PackageRevision = "1",

    [Parameter(Mandatory = $true)]
    [string] $OutputDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must use MAJOR.MINOR.PATCH format. Received: '$Version'."
}

if ($PackageRevision -notmatch '^[1-9]\d*$') {
    throw "PackageRevision must be a positive integer. Received: '$PackageRevision'."
}

$publishPath = (Resolve-Path -LiteralPath $PublishDir).Path
$binaryPath = Join-Path $publishPath "csharpfar"
if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
    throw "Published binary was not found: $binaryPath"
}

foreach ($command in @("dpkg-deb", "gzip", "chmod")) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found."
    }
}

$debianVersion = "$Version-$PackageRevision"
$outputPath = [System.IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$debPath = Join-Path $outputPath "csharpfar_${debianVersion}_amd64.deb"
$packageRoot = Join-Path ([System.IO.Path]::GetTempPath()) "csharpfar-deb-$([guid]::NewGuid().ToString('N'))"

try {
    $controlDir = Join-Path $packageRoot "DEBIAN"
    $binDir = Join-Path $packageRoot "usr/bin"
    $docDir = Join-Path $packageRoot "usr/share/doc/csharpfar"
    New-Item -ItemType Directory -Force -Path $controlDir, $binDir, $docDir | Out-Null

    $installedBinary = Join-Path $binDir "csharpfar"
    Copy-Item -LiteralPath $binaryPath -Destination $installedBinary

    $control = @"
Package: csharpfar
Version: $debianVersion
Section: utils
Priority: optional
Architecture: amd64
Maintainer: Dmitry Dorogoy <dorogoj@live.ru>
Homepage: https://github.com/DimonSmart/CSharpFar
Description: Experimental Far-like console file manager written in C#
 CSharpFar is an experimental two-panel console file manager inspired by Far Manager.
"@
    Set-Content -LiteralPath (Join-Path $controlDir "control") -Value $control -Encoding utf8NoBOM

    $changelogPath = Join-Path $docDir "changelog"
    $changelog = @"
csharpfar ($debianVersion) stable; urgency=medium

  * Release CSharpFar $Version.

 -- Dmitry Dorogoy <dorogoj@live.ru>  $([DateTimeOffset]::UtcNow.ToString('ddd, dd MMM yyyy HH:mm:ss +0000', [Globalization.CultureInfo]::InvariantCulture))
"@
    Set-Content -LiteralPath $changelogPath -Value $changelog -Encoding utf8NoBOM
    & gzip -9 -n -f $changelogPath
    if ($LASTEXITCODE -ne 0) {
        throw "gzip failed while creating changelog.gz."
    }

    $repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "../..")).Path
    $licensePath = Join-Path $repositoryRoot "LICENSE"
    $copyrightPath = Join-Path $docDir "copyright"
    if (Test-Path -LiteralPath $licensePath -PathType Leaf) {
        Copy-Item -LiteralPath $licensePath -Destination $copyrightPath
    }
    else {
        $copyright = @"
Upstream-Name: CSharpFar
Source: https://github.com/DimonSmart/CSharpFar

The upstream source tree did not contain a LICENSE file when this package was built.
Consult the upstream repository for current copyright and licensing information.
"@
        Set-Content -LiteralPath $copyrightPath -Value $copyright -Encoding utf8NoBOM
    }

    & chmod 0755 $installedBinary
    & chmod 0755 $packageRoot $controlDir $binDir $docDir (Split-Path $binDir) (Join-Path $packageRoot "usr/share")
    & chmod 0644 (Join-Path $controlDir "control") (Join-Path $docDir "changelog.gz") $copyrightPath
    if ($LASTEXITCODE -ne 0) {
        throw "chmod failed while setting package file modes."
    }

    $md5Lines = Get-ChildItem -LiteralPath (Join-Path $packageRoot "usr") -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($packageRoot, $_.FullName).Replace('\', '/')
            "$((Get-FileHash -LiteralPath $_.FullName -Algorithm MD5).Hash.ToLowerInvariant())  $relativePath"
        }
    Set-Content -LiteralPath (Join-Path $controlDir "md5sums") -Value $md5Lines -Encoding ascii
    & chmod 0644 (Join-Path $controlDir "md5sums")

    if (Test-Path -LiteralPath $debPath) {
        Remove-Item -LiteralPath $debPath -Force
    }
    & dpkg-deb --build --root-owner-group $packageRoot $debPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dpkg-deb failed to build '$debPath'."
    }

    & dpkg-deb --info $debPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dpkg-deb --info failed for '$debPath'."
    }
    & dpkg-deb --contents $debPath | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dpkg-deb --contents failed for '$debPath'."
    }

    Write-Output ([System.IO.Path]::GetFullPath($debPath))
}
finally {
    if (Test-Path -LiteralPath $packageRoot) {
        Remove-Item -LiteralPath $packageRoot -Recurse -Force
    }
}
