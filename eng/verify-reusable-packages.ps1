[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$packageDirectory = Join-Path $repositoryRoot 'artifacts/packages'
$globalPackagesDirectory = Join-Path $packageDirectory '.global-packages'
$nuGetConfigPath = Join-Path $packageDirectory 'NuGet.config'
$demoProject = Join-Path $repositoryRoot 'samples/CSharpFar.Ui.Demo/CSharpFar.Ui.Demo.csproj'
$projects = @(
    'src/CSharpFar.Console/CSharpFar.Console.csproj',
    'src/CSharpFar.Console.Ansi/CSharpFar.Console.Ansi.csproj',
    'src/CSharpFar.Console.Windows/CSharpFar.Console.Windows.csproj',
    'src/CSharpFar.Ui/CSharpFar.Ui.csproj'
)
$expectedPackages = @(
    'CSharpFar.Console.0.1.0.nupkg',
    'CSharpFar.Console.Ansi.0.1.0.nupkg',
    'CSharpFar.Console.Windows.0.1.0.nupkg',
    'CSharpFar.Ui.0.1.0.nupkg'
)
$forbiddenDependencies = @('CSharpFar.Core', 'CSharpFar.App', 'CSharpFar.FileSystem', 'CSharpFar.Shell')
$expectedDependencies = @{
    'CSharpFar.Console.0.1.0.nupkg' = @()
    'CSharpFar.Console.Ansi.0.1.0.nupkg' = @('CSharpFar.Console')
    'CSharpFar.Console.Windows.0.1.0.nupkg' = @('CSharpFar.Console')
    'CSharpFar.Ui.0.1.0.nupkg' = @('CSharpFar.Console', 'TextCopy')
}

Remove-Item -LiteralPath $packageDirectory -Recurse -Force -ErrorAction Ignore
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Push-Location $repositoryRoot
try {
    foreach ($project in $projects) {
        dotnet build $project --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $project." }
    }

    foreach ($project in $projects) {
        dotnet pack $project --configuration Release --no-build --output $packageDirectory
        if ($LASTEXITCODE -ne 0) { throw "Pack failed for $project." }
    }

    foreach ($package in $expectedPackages) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $package) -PathType Leaf)) {
            throw "Expected package '$package' was not created."
        }
    }

    Add-Type -AssemblyName System.IO.Compression
    foreach ($package in Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nupkg') {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $nuspec = $archive.Entries | Where-Object Name -like '*.nuspec' | Select-Object -First 1
            [xml]$metadata = (New-Object IO.StreamReader($nuspec.Open())).ReadToEnd()
            $dependencies = @($metadata.SelectNodes("//*[local-name()='dependency']"))
            foreach ($dependency in $dependencies) {
                if ($forbiddenDependencies -contains $dependency.id) { throw "Package '$($package.Name)' has forbidden dependency '$($dependency.id)'." }
            }
            $dependencyIds = @($dependencies | ForEach-Object id | Sort-Object)
            if (Compare-Object $expectedDependencies[$package.Name] $dependencyIds) {
                throw "Package '$($package.Name)' has an unexpected dependency graph: $($dependencyIds -join ', ')."
            }
            $libraryFiles = @($archive.Entries | Where-Object FullName -like 'lib/net10.0/*.dll')
            $assemblyName = ($package.BaseName -replace '\.0\.1\.0$', '') + '.dll'
            if ($libraryFiles.Count -ne 1 -or $libraryFiles[0].Name -ne $assemblyName) {
                throw "Package '$($package.Name)' does not contain exactly its own net10.0 library assembly."
            }
            $unexpectedEntries = @($archive.Entries | Where-Object {
                $_.FullName -notmatch '^(?:_rels/\.rels|\[Content_Types\]\.xml|package/services/metadata/core-properties/.*\.psmdcp|[^/]+\.nuspec|lib/net10\.0/[^/]+\.dll)$'
            })
            if ($unexpectedEntries) { throw "Package '$($package.Name)' contains unexpected assets: $($unexpectedEntries.FullName -join ', ')." }
        }
        finally { $archive.Dispose() }
    }

    $previousGlobalPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = $globalPackagesDirectory
    try {
        @(
            '<?xml version="1.0" encoding="utf-8"?>',
            '<configuration>',
            '  <packageSources>',
            '    <clear />',
            "    <add key=""local-reusable-packages"" value=""$packageDirectory"" />",
            '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />',
            '  </packageSources>',
            '</configuration>'
        ) | Set-Content -LiteralPath $nuGetConfigPath -Encoding utf8
        dotnet restore $demoProject --configfile $nuGetConfigPath --no-cache --force-evaluate -p:UseCSharpFarPackages=true
        if ($LASTEXITCODE -ne 0) { throw 'Package-mode restore failed.' }
        dotnet build $demoProject --configuration Release --no-restore -p:UseCSharpFarPackages=true
        if ($LASTEXITCODE -ne 0) { throw 'Package-mode build failed.' }
    }
    finally { $env:NUGET_PACKAGES = $previousGlobalPackages }

    $assetsPath = Join-Path $repositoryRoot 'samples/CSharpFar.Ui.Demo/obj/project.assets.json'
    $assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties.Name)
    foreach ($package in @('CSharpFar.Console/0.1.0', 'CSharpFar.Console.Ansi/0.1.0', 'CSharpFar.Console.Windows/0.1.0', 'CSharpFar.Ui/0.1.0')) {
        if ($libraries -notcontains $package) { throw "Package-mode assets did not resolve '$package'." }
    }
    if ($libraries | Where-Object { $_ -match '^CSharpFar\..*/project$' }) { throw 'Package-mode assets contain a CSharpFar project reference.' }
}
finally { Pop-Location }
