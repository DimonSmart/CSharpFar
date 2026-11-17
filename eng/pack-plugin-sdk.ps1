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
Write-Host "CSharpFar plugin SDK and template packages: $output"

# The template is distributed as a third, independent NuGet package.
$templateProject = Join-Path $root 'templates/CSharpFar.Plugin.Templates/CSharpFar.Plugin.Templates.csproj'
& dotnet pack $templateProject --configuration Release --output $output
if ($LASTEXITCODE -ne 0) { throw 'Packing CSharpFar.Plugin.Templates failed.' }
$templatePackages = @(Get-ChildItem -LiteralPath $output -Filter 'CSharpFar.Plugin.Templates.*.nupkg' -File)
if ($templatePackages.Count -ne 1) {
    throw 'Expected exactly one CSharpFar.Plugin.Templates NuGet package.'
}
