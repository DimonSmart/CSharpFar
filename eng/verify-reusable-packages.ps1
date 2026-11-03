[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$feed = Join-Path $root 'artifacts/packages'
$config = Join-Path $feed 'NuGet.config'
[xml]$props = Get-Content (Join-Path $root 'eng/TerminalPackages.props') -Raw
$version = [string]$props.Project.PropertyGroup.TerminalVersion
if (-not $version) { throw 'Missing TerminalVersion' }

$packages = [ordered]@{
  'DimonSmart.Terminal' = @{ Dll='CSharpFar.Console.dll'; Depends=@() }
  'DimonSmart.Terminal.Ansi' = @{ Dll='CSharpFar.Console.Ansi.dll'; Depends=@('DimonSmart.Terminal') }
  'DimonSmart.Terminal.Windows' = @{ Dll='CSharpFar.Console.Windows.dll'; Depends=@('DimonSmart.Terminal') }
  'DimonSmart.Terminal.Ui' = @{ Dll='CSharpFar.Ui.dll'; Depends=@('DimonSmart.Terminal','TextCopy') }
}
function RunDotnet([string[]]$arguments) {
  & dotnet @arguments
  if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments -join ' ') failed: $LASTEXITCODE" }
}
function CheckPackage([string]$id) {
  $path = Join-Path $feed "$id.$version.nupkg"
  if (-not (Test-Path $path -PathType Leaf)) { throw "Missing package $path" }
  $archive = [IO.Compression.ZipFile]::OpenRead($path)
  try {
    $dlls = @($archive.Entries.FullName | Where-Object { $_ -match '^lib/net10\.0/[^/]+\.dll$' })
    $expected = "lib/net10.0/$($packages[$id].Dll)"
    if ($dlls.Count -ne 1 -or $dlls[0] -ne $expected) {
      throw "$id must contain only $expected (actual $($dlls -join ', '))"
    }
    $entry = $archive.Entries | Where-Object Name -like '*.nuspec' | Select-Object -First 1
    if (-not $entry) { throw "Missing nuspec in $path" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    foreach ($tag in @('id','version','description','authors','tags','license','repository','readme')) {
      if (-not $nuspec.SelectSingleNode("//*[local-name()='$tag']")) { throw "$id missing $tag metadata" }
    }
    if ($nuspec.SelectSingleNode("//*[local-name()='id']").InnerText -ne $id) { throw "Wrong package ID: $id" }
    if ($nuspec.SelectSingleNode("//*[local-name()='version']").InnerText -ne $version) { throw "Wrong version for $id" }
    if ($nuspec.SelectSingleNode("//*[local-name()='license']").InnerText -ne 'MIT') { throw "Wrong license: $id" }
    $dependencies = @($nuspec.SelectNodes("//*[local-name()='dependency']"))
    $found = @( $dependencies | ForEach-Object { $_.GetAttribute('id') } | Sort-Object )
    $expectedDeps = @($packages[$id].Depends | Sort-Object)
    if (($found -join '|') -ne ($expectedDeps -join '|')) {
      throw "$id dependencies: expected $($expectedDeps -join ', '), found $($found -join ', ')"
    }
    foreach ($dep in $dependencies) {
      $depId = $dep.GetAttribute('id')
      $depVersion = $dep.GetAttribute('version')
      if ($depId -match '^CSharpFar(?:\.|$)') { throw "$id leaks a project package $depId" }
      if ($depId -like 'DimonSmart.Terminal*' -and $depVersion -notlike "*$version*") {
        throw "$id references $depId version $depVersion instead of $version"
      }
    }
  }
  finally { $archive.Dispose() }
}
function CheckConsumer([string]$name, [string[]]$requested, [string]$program) {
  $dir = Join-Path $feed "consumers/$name"
  New-Item -ItemType Directory -Path $dir -Force | Out-Null
  $refs = ($requested | ForEach-Object { '<PackageReference Include="' + $_ + '" Version="' + $version + '" />' }) -join [Environment]::NewLine
  $project = Join-Path $dir "$name.csproj"
  @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>$refs</ItemGroup>
</Project>
"@ | Set-Content $project -Encoding utf8
  $program | Set-Content (Join-Path $dir 'Program.cs') -Encoding utf8
  RunDotnet @('restore',$project,'--configfile',$config,'--no-cache','--force-evaluate')
  RunDotnet @('build',$project,'-c','Release','--no-restore')
  $assets = Get-Content (Join-Path $dir 'obj/project.assets.json') -Raw | ConvertFrom-Json
  $libraries = @($assets.libraries.PSObject.Properties.Name)
  $expected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($id in $requested) {
    [void]$expected.Add($id)
    if ($id -ne 'DimonSmart.Terminal') { [void]$expected.Add('DimonSmart.Terminal') }
  }
  foreach ($lib in $libraries | Where-Object { $_ -like 'DimonSmart.Terminal*' }) {
    $id, $v = $lib -split '/',2
    if (-not $expected.Contains($id) -or $v -ne $version) { throw "$name has unexpected package $lib" }
  }
  $target = @($assets.targets.PSObject.Properties.Value)[0]
  foreach ($id in $expected) {
    if ($libraries -notcontains "$id/$version") { throw "$name did not restore $id/$version" }
    $node = $target.PSObject.Properties["$id/$version"].Value
    if (-not $node) { throw "$name missing runtime target $id" }
    $dll = "lib/net10.0/$($packages[$id].Dll)"
    foreach ($asset in @('compile','runtime')) {
      if (@($node.$asset.PSObject.Properties.Name) -notcontains $dll) {
        throw "$name missing $asset asset $dll"
      }
    }
  }
}

Add-Type -AssemblyName System.IO.Compression
Remove-Item $feed -Recurse -Force -ErrorAction Ignore
New-Item -ItemType Directory -Path $feed -Force | Out-Null
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="DimonSmart.Terminal*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content $config -Encoding utf8

$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $feed '.global-packages'
Push-Location $root
try {
  foreach ($id in $packages.Keys) {
    RunDotnet @('pack',"src/$id/$id.csproj",'-c','Release','--output',$feed,'--configfile',$config)
    CheckPackage $id
  }

  CheckConsumer 'core' @('DimonSmart.Terminal') 'using CSharpFar.Console; _ = typeof(IConsoleDriver);'
  CheckConsumer 'ansi' @('DimonSmart.Terminal.Ansi') 'using CSharpFar.Console.Ansi; _ = typeof(AnsiTerminalConsoleDriver);'
  CheckConsumer 'windows' @('DimonSmart.Terminal.Windows') 'using CSharpFar.Console; _ = typeof(SystemConsoleDriver);'
  CheckConsumer 'ui-ansi' @('DimonSmart.Terminal.Ui','DimonSmart.Terminal.Ansi') 'using CSharpFar.Ui; using CSharpFar.Console.Ansi; _ = typeof(FormFieldFactory); _ = typeof(AnsiTerminalConsoleDriver);'
  CheckConsumer 'ui-windows' @('DimonSmart.Terminal.Ui','DimonSmart.Terminal.Windows') 'using CSharpFar.Ui; using CSharpFar.Console; _ = typeof(FormFieldFactory); _ = typeof(SystemConsoleDriver);'

  $demo = Join-Path $root 'samples/CSharpFar.Ui.Demo/CSharpFar.Ui.Demo.csproj'
  RunDotnet @('restore',$demo,'--configfile',$config,'--no-cache','--force-evaluate','-p:UseDimonSmartTerminalPackages=true')
  RunDotnet @('build',$demo,'-c','Release','--no-restore','-p:UseDimonSmartTerminalPackages=true')
  Write-Host 'Validated all four NuGet packages, five isolated consumers and the UI demo.'
}
finally {
  Pop-Location
  $env:NUGET_PACKAGES = $previousPackages
}
