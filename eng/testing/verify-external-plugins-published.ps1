[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Project,
    [Parameter(Mandatory=$true)][string]$RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$platform = if ($RuntimeIdentifier -like 'win-*') { 'Windows' }
    elseif ($RuntimeIdentifier -like 'linux-*') { 'Linux' }
    elseif ($RuntimeIdentifier -like 'osx-*') { 'MacOs' }
    else { throw "Unsupported runtime identifier $RuntimeIdentifier" }
$output = Join-Path $root "artifacts/external-singlefile/$RuntimeIdentifier"
$published = Join-Path $output 'published'
$config = Join-Path $output 'config'
Remove-Item $output -Recurse -Force -ErrorAction Ignore
New-Item -ItemType Directory -Path $published -Force | Out-Null

& dotnet publish (Join-Path $root $Project) -c Release -r $RuntimeIdentifier --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -o $published
if ($LASTEXITCODE -ne 0) { throw "Single-file publish failed: $RuntimeIdentifier" }
$executable = Join-Path $published $(if ($platform -eq 'Windows') { 'CSharpFar.exe' } else { 'csharpfar' })
if (-not (Test-Path $executable -PathType Leaf)) {
    throw "Single-file host executable was not produced: $executable"
}

foreach ($plugin in @('hello', 'goodbye')) {
    $source = Join-Path $root "artifacts/external-$plugin"
    $target = Join-Path $config "extensions/$plugin"
    if (-not (Test-Path (Join-Path $source 'plugin.json'))) { throw "Missing built plugin $plugin" }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item (Join-Path $source '*') -Destination $target -Recurse -Force
}

& $executable --self-test-extensions $config
if ($LASTEXITCODE -ne 0) { throw "External plugin single-file smoke failed: $RuntimeIdentifier" }
if (-not (Test-Path (Join-Path $config 'plugins/hello-world/disposed.marker'))) {
    throw "External plugin was not disposed."
}
Write-Host "PASS $RuntimeIdentifier : self-contained single-file host loaded and ran two external plugins."
