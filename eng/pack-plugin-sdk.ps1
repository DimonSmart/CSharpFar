[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/plugin-sdk')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null

foreach ($project in @('CSharpFar.Core', 'CSharpFar.Plugin.Abstractions')) {
    & dotnet pack (Join-Path $root "src/$project/$project.csproj") --configuration Release --output $output
    if ($LASTEXITCODE -ne 0) { throw "Packing $project failed." }
}
Write-Host "CSharpFar plugin contract packages: $output"
