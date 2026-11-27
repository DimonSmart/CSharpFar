[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][string]$Binary
)
$ErrorActionPreference = 'Stop'
$archivePath = (Resolve-Path -LiteralPath $Archive).Path
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("csharpfar-bundled-distribution-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    if ($archivePath.EndsWith('-app.zip')) { & /usr/bin/ditto -x -k $archivePath $temporary }
    elseif ($archivePath.EndsWith('.zip')) { Expand-Archive -LiteralPath $archivePath -DestinationPath $temporary }
    elseif ($archivePath.EndsWith('.deb')) { & dpkg-deb -x $archivePath $temporary }
    elseif ($archivePath.EndsWith('.tar.gz')) { & tar -xzf $archivePath -C $temporary }
    else { throw "Unsupported archive: $archivePath" }
    if (!$archivePath.EndsWith('.zip') -or $archivePath.EndsWith('-app.zip')) {
        if ($LASTEXITCODE -ne 0) { throw "Extraction failed: $archivePath" }
    }
    & "$PSScriptRoot/verify-bundled-extensions.ps1" -Executable (Join-Path $temporary $Binary)
}
finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
