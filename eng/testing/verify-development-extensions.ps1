$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $root
try {
    dotnet build CSharpFar.slnx -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Debug solution build failed.' }
    foreach ($platform in @('Windows', 'Linux', 'MacOs')) {
        $hostOutput = Join-Path $root "src/CSharpFar.Host.$platform/bin/Debug/net10.0"
        $extension = Join-Path $hostOutput 'development-extensions/azure-blob'
        foreach ($file in @('plugin.json','CSharpFar.Plugin.AzureBlob.dll','CSharpFar.Plugin.AzureBlob.deps.json','Azure.Storage.Blobs.dll','Azure.Identity.dll','Azure.Core.dll')) {
            if (!(Test-Path (Join-Path $extension $file) -PathType Leaf)) { throw "Missing development extension file: $platform/$file" }
        }
        foreach ($file in @('CSharpFar.Core.dll','CSharpFar.Plugin.Abstractions.dll')) {
            if (Test-Path (Join-Path $extension $file)) { throw "Shared contract copied into private extension: $file" }
        }
        foreach ($file in @('CSharpFar.Plugin.AzureBlob.dll','Azure.Storage.Blobs.dll','Azure.Identity.dll')) {
            if (Test-Path (Join-Path $hostOutput $file)) { throw "Extension leaked into host output: $platform/$file" }
        }
        $deps = Get-ChildItem $hostOutput -Filter '*.deps.json' | Select-Object -First 1
        if ((Get-Content $deps.FullName -Raw) -match 'Azure.Storage.Blobs|CSharpFar.Plugin.AzureBlob') { throw "Extension leaked into host dependency graph: $platform" }
        if (Test-Path (Join-Path $root "src/CSharpFar.Host.$platform/bin/Release/net10.0/development-extensions")) { throw "Development extension leaked into Release output: $platform" }
    }
} finally { Pop-Location }
