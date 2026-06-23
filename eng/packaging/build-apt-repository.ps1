[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DebsDir,

    [Parameter(Mandatory = $true)]
    [string] $SiteDir,

    [string] $Suite = "stable",
    [string] $Component = "main",
    [string] $Architecture = "amd64",
    [string] $Origin = "CSharpFar",
    [string] $Label = "CSharpFar"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

foreach ($command in @("apt-ftparchive", "gzip", "gpg")) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found."
    }
}

if ([string]::IsNullOrWhiteSpace($env:APT_GPG_FINGERPRINT)) {
    throw "APT_GPG_FINGERPRINT must identify the imported repository signing key."
}

$debsPath = (Resolve-Path -LiteralPath $DebsDir).Path
$debs = @(Get-ChildItem -LiteralPath $debsPath -File -Filter "csharpfar_*_${Architecture}.deb")
if ($debs.Count -eq 0) {
    throw "No csharpfar_*_${Architecture}.deb packages were found in '$debsPath'."
}

$sitePath = [System.IO.Path]::GetFullPath($SiteDir)
$aptPath = Join-Path $sitePath "apt"
$poolPath = Join-Path $aptPath "pool/$Component/c/csharpfar"
$distributionPath = Join-Path $aptPath "dists/$Suite"
$indexPath = Join-Path $distributionPath "$Component/binary-$Architecture"
$releasePath = Join-Path $distributionPath "Release"
$configPath = Join-Path ([System.IO.Path]::GetTempPath()) "csharpfar-apt-$([guid]::NewGuid().ToString('N')).conf"

New-Item -ItemType Directory -Force -Path $sitePath, $aptPath, $poolPath, $indexPath | Out-Null
New-Item -ItemType File -Force -Path (Join-Path $sitePath ".nojekyll") | Out-Null

$rootIndex = @"
<!doctype html><html lang="en"><meta charset="utf-8"><title>CSharpFar</title>
<h1>CSharpFar</h1><p><a href="apt/">APT repository</a></p>
"@
Set-Content -LiteralPath (Join-Path $sitePath "index.html") -Value $rootIndex -Encoding utf8NoBOM

$aptIndex = @"
<!doctype html><html lang="en"><meta charset="utf-8"><title>CSharpFar APT repository</title>
<h1>CSharpFar APT repository</h1>
<p>Install the public key in <code>/etc/apt/keyrings/csharpfar.gpg</code>, then add:</p>
<pre>deb [arch=$Architecture signed-by=/etc/apt/keyrings/csharpfar.gpg] https://dimonsmart.github.io/CSharpFar/apt $Suite $Component</pre>
"@
Set-Content -LiteralPath (Join-Path $aptPath "index.html") -Value $aptIndex -Encoding utf8NoBOM

$debs | Copy-Item -Destination $poolPath -Force

Push-Location $aptPath
try {
    & apt-ftparchive packages pool | Set-Content -LiteralPath "dists/$Suite/$Component/binary-$Architecture/Packages" -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) {
        throw "apt-ftparchive packages failed."
    }
    & gzip -9 -k -f "dists/$Suite/$Component/binary-$Architecture/Packages"
    if ($LASTEXITCODE -ne 0) {
        throw "gzip failed while creating Packages.gz."
    }

    $config = @"
APT::FTPArchive::Release::Origin "$Origin";
APT::FTPArchive::Release::Label "$Label";
APT::FTPArchive::Release::Suite "$Suite";
APT::FTPArchive::Release::Codename "$Suite";
APT::FTPArchive::Release::Architectures "$Architecture";
APT::FTPArchive::Release::Components "$Component";
APT::FTPArchive::Release::Description "CSharpFar APT repository";
"@
    Set-Content -LiteralPath $configPath -Value $config -Encoding utf8NoBOM
    & apt-ftparchive -c $configPath release "dists/$Suite" | Set-Content -LiteralPath "dists/$Suite/Release" -Encoding utf8NoBOM
    if ($LASTEXITCODE -ne 0) {
        throw "apt-ftparchive release failed."
    }
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $configPath -Force -ErrorAction SilentlyContinue
}

$gpgArguments = @("--batch", "--yes", "--local-user", $env:APT_GPG_FINGERPRINT)
if (-not [string]::IsNullOrEmpty($env:APT_GPG_PASSPHRASE)) {
    $gpgArguments += @("--pinentry-mode", "loopback", "--passphrase", $env:APT_GPG_PASSPHRASE)
}

& gpg @gpgArguments --output (Join-Path $distributionPath "Release.gpg") --detach-sign $releasePath
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create Release.gpg."
}
& gpg @gpgArguments --output (Join-Path $distributionPath "InRelease") --clearsign $releasePath
if ($LASTEXITCODE -ne 0) {
    throw "Failed to create InRelease."
}
& gpg --batch --yes --output (Join-Path $aptPath "csharpfar-archive-keyring.gpg") --export $env:APT_GPG_FINGERPRINT
if ($LASTEXITCODE -ne 0) {
    throw "Failed to export the binary public key."
}
& gpg --batch --yes --armor --output (Join-Path $aptPath "csharpfar-archive-keyring.asc") --export $env:APT_GPG_FINGERPRINT
if ($LASTEXITCODE -ne 0) {
    throw "Failed to export the armored public key."
}

$requiredFiles = @(
    "apt/dists/$Suite/InRelease",
    "apt/dists/$Suite/Release",
    "apt/dists/$Suite/Release.gpg",
    "apt/dists/$Suite/$Component/binary-$Architecture/Packages",
    "apt/dists/$Suite/$Component/binary-$Architecture/Packages.gz",
    "apt/csharpfar-archive-keyring.gpg",
    "apt/csharpfar-archive-keyring.asc"
)
foreach ($relativePath in $requiredFiles) {
    $requiredPath = Join-Path $sitePath $relativePath
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required repository file was not created: $requiredPath"
    }
}

$packages = Get-Content -LiteralPath (Join-Path $indexPath "Packages") -Raw
foreach ($pattern in @(
    '(?m)^Package: csharpfar$',
    "(?m)^Architecture: $([regex]::Escape($Architecture))$",
    "(?m)^Filename: pool/$([regex]::Escape($Component))/c/csharpfar/csharpfar_.*_$([regex]::Escape($Architecture))\.deb$"
)) {
    if ($packages -notmatch $pattern) {
        throw "Packages does not contain expected metadata matching '$pattern'."
    }
}

Write-Output $sitePath
