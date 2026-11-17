[CmdletBinding()]
param([string]$SdkFeed = 'artifacts/plugin-sdk')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$feed = Join-Path $root $SdkFeed
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'eng/PluginSdk.props') -Raw)).Project.PropertyGroup.PluginSdkVersion
if (-not $version) { throw 'SDK version is missing from eng/PluginSdk.props.' }

foreach ($name in @('CSharpFar.Core', 'CSharpFar.Plugin.Abstractions', 'CSharpFar.Plugin.Templates')) {
    $package = Join-Path $feed "$name.$version.nupkg"
    if (-not (Test-Path -LiteralPath $package)) { throw "Missing SDK package $package" }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($package)
    try {
        $paths = @($zip.Entries | ForEach-Object FullName)
        if ('README.md' -notin $paths) { throw "$name package has no README.md" }

        $nuspecEntry = $zip.Entries | Where-Object Name -EQ "$name.nuspec" | Select-Object -First 1
        if (-not $nuspecEntry) { throw "$name package has no nuspec" }
        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $id = $nuspec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='id']").InnerText
        $actualVersion = $nuspec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']").InnerText
        if ($id -ne $name -or $actualVersion -ne $version) {
            throw "$name has unexpected NuGet identity/version $id/$actualVersion"
        }

        if ($name -eq 'CSharpFar.Plugin.Templates') {
            if ('content/csharpfar-plugin/.template.config/template.json' -notin $paths -or
                'content/csharpfar-plugin/plugin.json' -notin $paths) {
                throw 'Plugin template package is missing its template definition or plugin manifest.'
            }
        }
        else {
            if ("lib/net10.0/$name.dll" -notin $paths) {
                throw "$name package is missing its public .NET 10 contract DLL."
            }
        }

        if ($name -eq 'CSharpFar.Plugin.Templates') {
            $projectFile = Join-Path $root 'templates/csharpfar-plugin/CSharpFar.Plugin.Template.csproj'
            [xml]$templateProject = Get-Content -LiteralPath $projectFile -Raw
            $sdkReference = @($templateProject.Project.ItemGroup.PackageReference |
                Where-Object Include -EQ 'CSharpFar.Plugin.Abstractions')
            if ($sdkReference.Count -ne 1 -or $sdkReference[0].Version -ne $version) {
                throw 'Update the generated template dependency when the SDK version changes.'
            }
        }

        if ($name -eq 'CSharpFar.Plugin.Abstractions') {
            $dependency = $nuspec.SelectSingleNode(
                "//*[local-name()='dependency' and @id='CSharpFar.Core']")
            if (-not $dependency) { throw 'Plugin abstractions must depend on CSharpFar.Core.' }
            if (-not $dependency.GetAttribute('version').Contains($version)) {
                throw 'Plugin abstractions depend on the wrong CSharpFar.Core version.'
            }
        }
    }
    finally { $zip.Dispose() }
}
Write-Host 'PASS: SDK packages contain the expected assemblies, metadata, dependencies and template.'
