[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [string[]]$ExpectedIds = @('azure-blob')
)
$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$config = Join-Path ([IO.Path]::GetTempPath()) ("csharpfar-bundled-config-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $config | Out-Null
try {
    & $executablePath --self-test-bundled-extensions $config @ExpectedIds
    if ($LASTEXITCODE -ne 0) { throw "Bundled extension smoke test failed: $executablePath" }
    if (Test-Path -LiteralPath (Join-Path $config 'extensions')) { throw 'Bundled test created user-installed extensions.' }
}
finally { Remove-Item -LiteralPath $config -Recurse -Force }
