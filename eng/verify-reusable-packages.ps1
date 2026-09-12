[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$feed = Join-Path $root 'artifacts/packages'
$config = Join-Path $feed 'NuGet.config'
$demo = Join-Path $root 'samples/CSharpFar.Ui.Demo/CSharpFar.Ui.Demo.csproj'
$terminalProject = Join-Path $root 'src/DimonSmart.Terminal/DimonSmart.Terminal.csproj'

function Get-MSBuildProperty([string] $project, [string] $name) {
    $output = @(& dotnet msbuild $project -nologo "-getProperty:$name")
    if ($LASTEXITCODE) { throw "Failed to read MSBuild property $name from $project." }

    $values = @($output | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($values.Count -ne 1) {
        throw "Expected one value for MSBuild property $name, got: $($values -join ', ')."
    }

    return $values[0]
}

function Get-Archive([string] $path, [scriptblock] $action) {
    Add-Type -AssemblyName System.IO.Compression
    $archive = [System.IO.Compression.ZipFile]::OpenRead($path)
    try { & $action $archive } finally { $archive.Dispose() }
}

function Get-Nuspec([string] $path) {
    Get-Archive $path {
        param($archive)
        $entry = $archive.Entries | Where-Object Name -like '*.nuspec' | Select-Object -First 1
        if ($null -eq $entry) { throw "Package $path has no nuspec." }

        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml] $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
}

function Assert-PackageEntry([string[]] $entries, [string] $entry, [string] $packageName) {
    if ($entries -notcontains $entry) {
        throw "$packageName is missing $entry."
    }
}

function Assert-PortablePdb([string] $packagePath, [string] $entryName) {
    Get-Archive $packagePath {
        param($archive)
        $entry = $archive.Entries | Where-Object FullName -eq $entryName | Select-Object -First 1
        if ($null -eq $entry) { throw "$packagePath is missing $entryName." }

        $stream = $entry.Open()
        try {
            $header = [byte[]]::new(4)
            if ($stream.Read($header, 0, $header.Length) -ne $header.Length) {
                throw "$entryName is too short to be a portable PDB."
            }

            if ([Text.Encoding]::ASCII.GetString($header) -ne 'BSJB') {
                throw "$entryName is not a portable PDB."
            }
        } finally {
            $stream.Dispose()
        }
    }
}

$packageVersion = Get-MSBuildProperty $terminalProject 'DimonSmartTerminalPackageVersion'
if ([string]::IsNullOrWhiteSpace($packageVersion)) { throw 'Reusable package version is empty.' }

$terminalPackage = "DimonSmart.Terminal.$packageVersion.nupkg"
$uiPackage = "DimonSmart.Terminal.Ui.$packageVersion.nupkg"
$repositoryCommit = @(& git -C $root rev-parse HEAD)[0].Trim()
if ($LASTEXITCODE -or [string]::IsNullOrWhiteSpace($repositoryCommit)) {
    throw 'Could not determine the repository commit.'
}

Remove-Item -LiteralPath $feed -Recurse -Force -ErrorAction Ignore
New-Item -ItemType Directory -Path $feed -Force | Out-Null

@(
    '<?xml version="1.0" encoding="utf-8"?>',
    '<configuration>',
    '  <packageSources>',
    '    <clear />',
    "    <add key=""local"" value=""$feed"" />",
    '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />',
    '  </packageSources>',
    '  <packageSourceMapping>',
    '    <packageSource key="local">',
    '      <package pattern="DimonSmart.Terminal" />',
    '      <package pattern="DimonSmart.Terminal.Ui" />',
    '    </packageSource>',
    '    <packageSource key="nuget.org">',
    '      <package pattern="TextCopy" />',
    '      <package pattern="Microsoft.*" />',
    '      <package pattern="System.*" />',
    '    </packageSource>',
    '  </packageSourceMapping>',
    '</configuration>'
) | Set-Content -LiteralPath $config -Encoding utf8

Push-Location $root
try {
    dotnet pack src/DimonSmart.Terminal/DimonSmart.Terminal.csproj -c Release --output $feed "-p:RepositoryCommit=$repositoryCommit"
    if ($LASTEXITCODE) { throw 'Terminal package build failed.' }

    dotnet pack src/DimonSmart.Terminal.Ui/DimonSmart.Terminal.Ui.csproj -c Release --output $feed --configfile $config "-p:RepositoryCommit=$repositoryCommit"
    if ($LASTEXITCODE) { throw 'UI package build failed.' }

    foreach ($name in @($terminalPackage, $uiPackage)) {
        if (-not (Test-Path -LiteralPath (Join-Path $feed $name) -PathType Leaf)) {
            throw "Missing $name."
        }
    }

    $terminalPath = Join-Path $feed $terminalPackage
    $uiPath = Join-Path $feed $uiPackage
    $terminalEntries = Get-Archive $terminalPath { param($archive) @($archive.Entries | ForEach-Object FullName) }
    $uiEntries = Get-Archive $uiPath { param($archive) @($archive.Entries | ForEach-Object FullName) }

    foreach ($assembly in @('CSharpFar.Console', 'CSharpFar.Console.Ansi', 'CSharpFar.Console.Windows')) {
        foreach ($extension in @('dll', 'pdb', 'xml')) {
            Assert-PackageEntry $terminalEntries "lib/net10.0/$assembly.$extension" $terminalPackage
        }
        Assert-PortablePdb $terminalPath "lib/net10.0/$assembly.pdb"
    }

    foreach ($extension in @('dll', 'pdb', 'xml')) {
        Assert-PackageEntry $uiEntries "lib/net10.0/CSharpFar.Ui.$extension" $uiPackage
    }
    Assert-PortablePdb $uiPath 'lib/net10.0/CSharpFar.Ui.pdb'

    Assert-PackageEntry $terminalEntries 'README.md' $terminalPackage
    Assert-PackageEntry $uiEntries 'README.md' $uiPackage

    foreach ($entry in @($terminalEntries + $uiEntries)) {
        if ($entry -match '^lib/net10\.0/CSharpFar\.(?:App|Core|Platform(?:\.|$)|Module(?:\.|$)).*\.dll$') {
            throw "Reusable package contains product-specific assembly $entry."
        }
    }

    $terminalNuspec = Get-Nuspec $terminalPath
    $uiNuspec = Get-Nuspec $uiPath
    foreach ($nuspec in @($terminalNuspec, $uiNuspec)) {
        foreach ($element in @('id', 'version', 'authors', 'license', 'readme', 'description', 'tags', 'repository')) {
            if ($null -eq $nuspec.SelectSingleNode("//*[local-name()='$element']")) {
                throw "Package metadata is missing $element."
            }
        }

        if ($nuspec.SelectSingleNode("//*[local-name()='version']").InnerText -ne $packageVersion) {
            throw "Package version is not the coordinated version $packageVersion."
        }
        if ($nuspec.SelectSingleNode("//*[local-name()='license']").InnerText -ne 'MIT') {
            throw 'Package license must be MIT.'
        }

        $repository = $nuspec.SelectSingleNode("//*[local-name()='repository']")
        if ([string]::IsNullOrWhiteSpace($repository.GetAttribute('url'))) {
            throw 'Package repository URL is missing.'
        }
        if ($repository.GetAttribute('commit') -ne $repositoryCommit) {
            throw "Package repository commit does not match $repositoryCommit."
        }

        if (@($nuspec.SelectNodes("//*[local-name()='dependency']") | Where-Object id -match '^CSharpFar(?:\.|$)')) {
            throw 'Package has a forbidden CSharpFar package dependency.'
        }
    }

    if (@($terminalNuspec.SelectNodes("//*[local-name()='dependency']")).Count) {
        throw 'Terminal must have no NuGet dependencies.'
    }

    $uiDependencyNodes = @($uiNuspec.SelectNodes("//*[local-name()='dependency']"))
    $uiDependencies = @($uiDependencyNodes | ForEach-Object id)
    if ('DimonSmart.Terminal' -notin $uiDependencies -or 'TextCopy' -notin $uiDependencies) {
        throw 'UI package dependency graph is incomplete.'
    }

    $terminalDependency = $uiDependencyNodes | Where-Object id -eq 'DimonSmart.Terminal' | Select-Object -First 1
    if ($null -eq $terminalDependency -or $terminalDependency.version -notmatch [regex]::Escape($packageVersion)) {
        throw "UI package does not depend on coordinated terminal version $packageVersion."
    }

    $previousPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = Join-Path $feed '.global-packages'
    try {
        dotnet restore $demo --configfile $config --no-cache --force-evaluate -p:UseDimonSmartTerminalPackages=true
        if ($LASTEXITCODE) { throw 'Package-mode restore failed.' }

        dotnet build $demo -c Release --no-restore -p:UseDimonSmartTerminalPackages=true
        if ($LASTEXITCODE) { throw 'Package-mode build failed.' }
    } finally {
        $env:NUGET_PACKAGES = $previousPackages
    }

    $assets = Get-Content -Raw (Join-Path $root 'samples/CSharpFar.Ui.Demo/obj/project.assets.json') | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties.Name)
    foreach ($package in @("DimonSmart.Terminal/$packageVersion", "DimonSmart.Terminal.Ui/$packageVersion")) {
        if ($libraries -notcontains $package) {
            throw "Assets did not resolve $package."
        }
    }

    if ($libraries | Where-Object { $_ -match '^CSharpFar\.(?:Console|Console\.Ansi|Console\.Windows|Ui)/.*/project$' }) {
        throw 'Assets contain a reusable ProjectReference.'
    }
} finally {
    Pop-Location
}
