[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$PublishDir,
    [Parameter(Mandatory=$true)][string[]]$Packages
)
$ErrorActionPreference = 'Stop'
$destination = Join-Path (Resolve-Path -LiteralPath $PublishDir).Path 'bundled-extensions'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ("csharpfar-bundled-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $staged = Join-Path $temporary 'staged'
    New-Item -ItemType Directory -Path $staged | Out-Null
    foreach ($package in $Packages) {
        $zip = (Resolve-Path -LiteralPath $package).Path
        $checksum = (Get-Content -LiteralPath "$zip.sha256" -Raw).Trim() -split '\s+'
        if ($checksum.Count -ne 2 -or $checksum[1] -ne (Split-Path $zip -Leaf) -or
            (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $checksum[0]) {
            throw "Invalid extension checksum: $zip"
        }
        $unpacked = Join-Path $temporary ([guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $zip -DestinationPath $unpacked
        $children = @(Get-ChildItem -LiteralPath $unpacked -Force)
        if ($children.Count -ne 1 -or !$children[0].PSIsContainer) { throw "Expected one extension directory: $zip" }
        $folder = $children[0]
        $manifest = Get-Content -LiteralPath (Join-Path $folder.FullName 'plugin.json') -Raw | ConvertFrom-Json
        if ($manifest.id -notmatch '^[a-zA-Z0-9_.-]+$' -or $manifest.id -in @('.', '..') -or
            $manifest.id -cne $folder.Name -or !$ids.Add($manifest.id) -or $manifest.apiVersion -ne 2 -or
            $manifest.assembly -notmatch '^[a-zA-Z0-9_.-]+\.dll$' -or
            !(Test-Path -LiteralPath (Join-Path $folder.FullName $manifest.assembly) -PathType Leaf)) {
            throw "Invalid or duplicate bundled extension manifest: $zip"
        }
        foreach ($contract in @('CSharpFar.Core.dll', 'CSharpFar.Plugin.Abstractions.dll')) {
            if (Get-ChildItem -LiteralPath $folder.FullName -Filter $contract -File -Recurse) {
                throw "Bundled extension contains shared SDK contract: $contract"
            }
        }
        Get-ChildItem -LiteralPath $folder.FullName -Filter '*.pdb' -File -Recurse | Remove-Item -Force
        Copy-Item -LiteralPath $folder.FullName -Destination $staged -Recurse
    }
    if ($ids.Count -eq 0) { throw 'No bundled packages provided.' }
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    Copy-Item -LiteralPath $staged -Destination $destination -Recurse
    Write-Host ("Staged bundled extensions: " + ($ids -join ', '))
}
finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
