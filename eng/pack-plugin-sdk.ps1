[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts/plugin-sdk')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null

# Never publish stale packages left over from previous invocations.
foreach ($name in @('CSharpFar.Core', 'CSharpFar.Plugin.Abstractions', 'CSharpFar.Plugin.Templates')) {
    Get-ChildItem -LiteralPath $output -Filter "$name.*.nupkg" -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

foreach ($project in @('CSharpFar.Core', 'CSharpFar.Plugin.Abstractions')) {
    & dotnet pack (Join-Path $root "src/$project/$project.csproj") --configuration Release --output $output
    if ($LASTEXITCODE -ne 0) { throw "Packing $project failed." }
    $packages = @(Get-ChildItem -LiteralPath $output -Filter "$project.*.nupkg" -File)
    if ($packages.Count -ne 1) { throw "Expected exactly one package for $project." }
}

# The template is distributed as a third, independent NuGet package.
$templateProject = Join-Path $root 'templates/CSharpFar.Plugin.Templates/CSharpFar.Plugin.Templates.csproj'
& dotnet pack $templateProject --configuration Release --output $output
if ($LASTEXITCODE -ne 0) { throw 'Packing CSharpFar.Plugin.Templates failed.' }
$templatePackages = @(Get-ChildItem -LiteralPath $output -Filter 'CSharpFar.Plugin.Templates.*.nupkg' -File)
if ($templatePackages.Count -ne 1) {
    throw 'Expected exactly one CSharpFar.Plugin.Templates NuGet package.'
}

Write-Host "CSharpFar plugin SDK and template packages: $output"
