[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$feed = Join-Path $root 'artifacts/packages'
$config = Join-Path $feed 'NuGet.config'
$demo = Join-Path $root 'samples/CSharpFar.Ui.Demo/CSharpFar.Ui.Demo.csproj'
$terminalPackage = 'DimonSmart.Terminal.0.1.0-beta.1.nupkg'
$uiPackage = 'DimonSmart.Terminal.Ui.0.1.0-beta.1.nupkg'

function Get-Archive([string] $path, [scriptblock] $action) {
    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($path)
    try { & $action $archive } finally { $archive.Dispose() }
}

function Get-Nuspec([string] $path) {
    Get-Archive $path {
        param($archive)
        $entry = $archive.Entries | Where-Object Name -like '*.nuspec' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml] $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
}

Remove-Item -LiteralPath $feed -Recurse -Force -ErrorAction Ignore
New-Item -ItemType Directory -Path $feed -Force | Out-Null
@(
    '<?xml version="1.0" encoding="utf-8"?>', '<configuration>', '  <packageSources>', '    <clear />',
    "    <add key=""local"" value=""$feed"" />", '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />',
    '  </packageSources>', '</configuration>'
) | Set-Content -LiteralPath $config -Encoding utf8

Push-Location $root
try {
    dotnet pack src/DimonSmart.Terminal/DimonSmart.Terminal.csproj -c Release --output $feed
    if ($LASTEXITCODE) { throw 'Terminal package build failed.' }
    dotnet pack src/DimonSmart.Terminal.Ui/DimonSmart.Terminal.Ui.csproj -c Release --output $feed --configfile $config
    if ($LASTEXITCODE) { throw 'UI package build failed.' }

    foreach ($name in @($terminalPackage, $uiPackage)) {
        if (-not (Test-Path -LiteralPath (Join-Path $feed $name) -PathType Leaf)) { throw "Missing $name." }
    }
    $terminalPath = Join-Path $feed $terminalPackage
    $uiPath = Join-Path $feed $uiPackage
    $terminalEntries = Get-Archive $terminalPath { param($archive) @($archive.Entries | ForEach-Object FullName) }
    foreach ($assembly in @('CSharpFar.Console.dll', 'CSharpFar.Console.Ansi.dll', 'CSharpFar.Console.Windows.dll')) {
        if ($terminalEntries -notcontains "lib/net10.0/$assembly") { throw "Terminal package is missing $assembly." }
    }
    $uiEntries = Get-Archive $uiPath { param($archive) @($archive.Entries | ForEach-Object FullName) }
    if ($uiEntries -notcontains 'lib/net10.0/CSharpFar.Ui.dll') { throw 'UI package is missing CSharpFar.Ui.dll.' }

    $terminalNuspec = Get-Nuspec $terminalPath
    $uiNuspec = Get-Nuspec $uiPath
    foreach ($nuspec in @($terminalNuspec, $uiNuspec)) {
        foreach ($element in @('id', 'version', 'authors', 'license', 'readme', 'description', 'tags', 'repository')) {
            if ($null -eq $nuspec.SelectSingleNode("//*[local-name()='$element']")) { throw "Package metadata is missing $element." }
        }
        if ($nuspec.SelectSingleNode("//*[local-name()='license']").InnerText -ne 'MIT') { throw 'Package license must be MIT.' }
        if (@($nuspec.SelectNodes("//*[local-name()='dependency']") | Where-Object id -match '^CSharpFar(?:\.|$)')) { throw 'Package has a forbidden CSharpFar dependency.' }
    }
    if (@($terminalNuspec.SelectNodes("//*[local-name()='dependency']")).Count) { throw 'Terminal must have no NuGet dependencies.' }
    $uiDependencies = @($uiNuspec.SelectNodes("//*[local-name()='dependency']") | ForEach-Object id)
    if ('DimonSmart.Terminal' -notin $uiDependencies -or 'TextCopy' -notin $uiDependencies) { throw 'UI package dependency graph is incomplete.' }

    $previousPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = Join-Path $feed '.global-packages'
    try {
        dotnet restore $demo --configfile $config --no-cache --force-evaluate -p:UseDimonSmartTerminalPackages=true
        if ($LASTEXITCODE) { throw 'Package-mode restore failed.' }
        dotnet build $demo -c Release --no-restore -p:UseDimonSmartTerminalPackages=true
        if ($LASTEXITCODE) { throw 'Package-mode build failed.' }
    } finally { $env:NUGET_PACKAGES = $previousPackages }

    $assets = Get-Content -Raw (Join-Path $root 'samples/CSharpFar.Ui.Demo/obj/project.assets.json') | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties.Name)
    foreach ($package in @('DimonSmart.Terminal/0.1.0-beta.1', 'DimonSmart.Terminal.Ui/0.1.0-beta.1')) {
        if ($libraries -notcontains $package) { throw "Assets did not resolve $package." }
    }
    if ($libraries | Where-Object { $_ -match '^CSharpFar\.(?:Console|Console\.Ansi|Console\.Windows|Ui)/.*/project$' }) { throw 'Assets contain a reusable ProjectReference.' }
} finally { Pop-Location }
