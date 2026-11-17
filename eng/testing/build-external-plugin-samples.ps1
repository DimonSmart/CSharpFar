[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& (Join-Path $root 'eng/pack-plugin-sdk.ps1')
if ($LASTEXITCODE -ne 0) { throw 'SDK packaging failed.' }
$feed = Join-Path $root 'artifacts/plugin-sdk'

foreach ($plugin in @('HelloWorld', 'GoodbyeWorld')) {
    $project = Join-Path $root "samples/CSharpFar.Plugin.$plugin/CSharpFar.Plugin.$plugin.csproj"
    $outputName = $plugin.ToLowerInvariant().Replace('world', '')
    $output = Join-Path $root "artifacts/external-$outputName"
    & dotnet restore $project --source $feed
    if ($LASTEXITCODE -ne 0) { throw "External sample restore failed: $plugin" }
    & dotnet publish $project -c Release --no-restore -o $output
    if ($LASTEXITCODE -ne 0) { throw "External sample publish failed: $plugin" }
    Copy-Item -LiteralPath (Join-Path $root "samples/CSharpFar.Plugin.$plugin/plugin.json") -Destination $output -Force
    if (-not (Test-Path -LiteralPath (Join-Path $output 'plugin.json'))) {
        throw "Missing external plugin manifest: $plugin"
    }
}
Write-Host 'PASS: independent binary-SDK plugins built.'
