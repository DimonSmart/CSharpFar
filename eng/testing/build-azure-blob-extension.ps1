[CmdletBinding()]
param([string]$OutputDirectory, [string]$SdkRelease = 'v1.0.129')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/azure-blob' }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$feed = Join-Path $root "artifacts/azure-sdk-feed/$SdkRelease"
New-Item -ItemType Directory -Path $feed -Force | Out-Null
$base = "https://github.com/DimonSmart/CSharpFar/releases/download/$SdkRelease"
$checksumFile = Join-Path $feed 'SHA256SUMS.txt'
Invoke-WebRequest "$base/SHA256SUMS.txt" -OutFile $checksumFile
foreach ($name in @('CSharpFar.Core.2.0.0.nupkg', 'CSharpFar.Plugin.Abstractions.2.0.0.nupkg')) {
    $file = Join-Path $feed $name
    Invoke-WebRequest "$base/$name" -OutFile $file
    $line = Get-Content $checksumFile | Where-Object { $_ -match ('\s+' + [regex]::Escape($name) + '$') } | Select-Object -First 1
    if (!$line -or (Get-FileHash $file -Algorithm SHA256).Hash -ne ($line -split '\s+')[0]) {
        throw "SDK checksum verification failed: $name"
    }
}
$project = Join-Path $root 'src/CSharpFar.Plugin.AzureBlob/CSharpFar.Plugin.AzureBlob.csproj'
# The host solution is not restored or built by this script.
& dotnet restore $project -p:AzureBlobBinarySdk=true --source $feed --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) { throw 'Azure Blob SDK restore failed.' }
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
$folder = Join-Path $output 'azure-blob'
& dotnet publish $project -p:AzureBlobBinarySdk=true -c Release --no-restore -o $folder
if ($LASTEXITCODE -ne 0) { throw 'Azure Blob publish failed.' }
foreach ($contract in @('CSharpFar.Core.dll', 'CSharpFar.Plugin.Abstractions.dll')) {
    Remove-Item (Join-Path $folder $contract) -Force -ErrorAction SilentlyContinue
}
foreach ($name in @('plugin.json','CSharpFar.Plugin.AzureBlob.dll','CSharpFar.Plugin.AzureBlob.deps.json','Azure.Storage.Blobs.dll','Azure.Identity.dll','Azure.Core.dll')) {
    if (!(Test-Path (Join-Path $folder $name) -PathType Leaf)) { throw "Missing plugin dependency: $name" }
}
$manifest = Get-Content (Join-Path $folder 'plugin.json') -Raw | ConvertFrom-Json
if ($manifest.apiVersion -ne 2 -or $manifest.assembly -ne 'CSharpFar.Plugin.AzureBlob.dll') { throw 'Unexpected manifest.' }
Copy-Item (Join-Path $root 'src/CSharpFar.Plugin.AzureBlob/README.md') $folder
Copy-Item (Join-Path $root 'src/CSharpFar.Plugin.AzureBlob/sdk-feedback.md') $folder
Copy-Item (Join-Path $root 'LICENSE') $folder
$zip = Join-Path $output "CSharpFar.AzureBlobPlugin-$($manifest.version).zip"
Compress-Archive -Path $folder -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([System.IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding ascii
Write-Host "Extension built against published binary SDK: $zip"
