[CmdletBinding()]
param([string]$SdkFeed = 'artifacts/plugin-sdk')

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$feed = Join-Path $root $SdkFeed
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'eng/PluginSdk.props') -Raw)).Project.PropertyGroup.PluginSdkVersion
$package = Join-Path $feed "CSharpFar.Plugin.Templates.$version.nupkg"
if (-not (Test-Path -LiteralPath $package)) {
    throw "Template NuGet package not found: $package"
}

$generated = Join-Path $root 'artifacts/generated-plugin'
$published = Join-Path $root 'artifacts/generated-plugin-published'
Remove-Item -LiteralPath $generated, $published -Recurse -Force -ErrorAction SilentlyContinue

& dotnet new install $package --force
if ($LASTEXITCODE -ne 0) { throw 'Cannot install the packaged CSharpFar template.' }
& dotnet new csharpfar-plugin --name 'Independent.GeneratedPlugin' --output $generated --PluginId 'ci-generated' --PluginTitle 'CI Generated' --force
if ($LASTEXITCODE -ne 0) { throw 'Cannot instantiate packaged plugin template.' }

$project = Join-Path $generated 'Independent.GeneratedPlugin.csproj'
& dotnet restore $project --source $feed
if ($LASTEXITCODE -ne 0) { throw 'Generated plugin failed to restore from the binary SDK feed.' }
& dotnet publish $project --configuration Release --no-restore --output $published
if ($LASTEXITCODE -ne 0) { throw 'Generated plugin failed to publish.' }

$manifestPath = Join-Path $published 'plugin.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Published plugin is missing its manifest.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.id -ne 'ci-generated' -or
    $manifest.name -ne 'CI Generated' -or
    $manifest.assembly -ne 'Independent.GeneratedPlugin.dll' -or
    $manifest.entryType -ne 'Independent.GeneratedPlugin.Plugin' -or
    $manifest.apiVersion -ne 2) {
    throw "Generated plugin manifest is invalid: $($manifest | ConvertTo-Json -Compress)"
}
if (-not (Test-Path -LiteralPath (Join-Path $published $manifest.assembly))) {
    throw 'Generated plugin assembly not found.'
}

Write-Host 'PASS: packaged dotnet new template restores, publishes and includes a valid manifest.'
