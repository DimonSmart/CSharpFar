[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string] $Project,
  [Parameter(Mandatory=$true)][string] $RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $root $Project
if (-not (Test-Path $projectPath)) { throw "Host project does not exist: $Project" }
$platform = if ($RuntimeIdentifier -like 'win-*') { 'Windows' }
  elseif ($RuntimeIdentifier -like 'linux-*') { 'Linux' }
  elseif ($RuntimeIdentifier -like 'osx-*') { 'MacOs' }
  else { throw "Unexpected RID: $RuntimeIdentifier" }
if ($Project -notlike "*CSharpFar.Host.$platform/*" -and $Project -notlike "*CSharpFar.Host.$platform\*") {
  throw "Host project $Project does not match $RuntimeIdentifier"
}

$expected = if ($platform -eq 'Windows') {
  @('CSharpFar.Platform.Windows','CSharpFar.Console.Windows','CSharpFar.NetworkTraffic.Windows')
} elseif ($platform -eq 'Linux') {
  @('CSharpFar.Platform.Linux','CSharpFar.Console.Ansi')
} else {
  @('CSharpFar.Platform.MacOs','CSharpFar.Console.Ansi')
}

$allPlatformLibraries = @(
  'CSharpFar.Platform.Windows','CSharpFar.Console.Windows','CSharpFar.NetworkTraffic.Windows',
  'CSharpFar.Platform.Linux','CSharpFar.Platform.MacOs','CSharpFar.Console.Ansi'
)
$forbidden = @($allPlatformLibraries | Where-Object { $_ -notin $expected })
if ($platform -ne 'Windows') {
  $forbidden += @('Microsoft.Diagnostics.Tracing.TraceEvent','System.Security.Cryptography.ProtectedData')
}
$out = Join-Path $root "artifacts/platform-closure/$RuntimeIdentifier"
Remove-Item -LiteralPath $out -Recurse -Force -ErrorAction Ignore
New-Item -ItemType Directory -Path $out -Force | Out-Null

Push-Location $root
try {
  & dotnet publish $projectPath -c Release -r $RuntimeIdentifier --self-contained false -o $out -p:PublishSingleFile=false -p:PublishReadyToRun=false
  if ($LASTEXITCODE -ne 0) { throw "Diagnostic publish failed for $RuntimeIdentifier" }
}
finally { Pop-Location }

$assetPath = Join-Path (Split-Path $projectPath -Parent) 'obj/project.assets.json'
$assets = Get-Content -Raw $assetPath | ConvertFrom-Json
$ridTarget = @($assets.targets.PSObject.Properties | Where-Object Name -like "*/$RuntimeIdentifier" | Select-Object -First 1)
if ($ridTarget.Count -ne 1) { throw "Missing $RuntimeIdentifier restore target in $assetPath" }
$assetIds = @($ridTarget[0].Value.PSObject.Properties.Name | ForEach-Object { ($_ -split '/')[0] })
$depsFiles = @(Get-ChildItem -LiteralPath $out -Filter '*.deps.json' -File)
if ($depsFiles.Count -ne 1) { throw "Expected one .deps.json in $out" }
$deps = Get-Content -LiteralPath $depsFiles[0].FullName -Raw | ConvertFrom-Json
$depsIds = @($deps.libraries.PSObject.Properties.Name | ForEach-Object { ($_ -split '/')[0] })
$dlls = @((Get-ChildItem -LiteralPath $out -Filter '*.dll' -File) | ForEach-Object BaseName)

foreach ($id in $forbidden) {
  if ($id -in $assetIds) { throw "$RuntimeIdentifier restore includes forbidden $id" }
  if ($id -in $depsIds) { throw "$RuntimeIdentifier .deps.json includes forbidden $id" }
  if ($id -in $dlls) { throw "$RuntimeIdentifier publish includes forbidden $id.dll" }
}
foreach ($id in $expected) {
  if ($id -notin $assetIds) { throw "$RuntimeIdentifier restore is missing $id" }
  if ($id -notin $depsIds) { throw "$RuntimeIdentifier .deps.json is missing $id" }
  if ($id -notin $dlls) { throw "$RuntimeIdentifier publish is missing $id.dll" }
}

$dll = if ($platform -eq 'Windows') { 'CSharpFar.dll' } else { 'csharpfar.dll' }
& dotnet (Join-Path $out $dll) --self-test
if ($LASTEXITCODE -ne 0) { throw "$RuntimeIdentifier --self-test failed" }
Write-Host "PASS ${RuntimeIdentifier}: restored, deps.json and published runtime closure contain only permitted platform dependencies."
