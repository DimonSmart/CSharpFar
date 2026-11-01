# Download immutable test samples without committing third-party binaries.
# Git blob identities protect against unexpected upstream fixture changes.
$ErrorActionPreference = "Stop"
$root = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "tests/archive-fixtures"
New-Item -ItemType Directory -Path $root -Force | Out-Null
$commit = "b4af8afe6683d019d504954a3b4e4f18b06e2cd6"
$fixtures = @(
    @{ Name = "Rar.rar"; Sha = "050509c347d6911db98f13db8aa33c8f914ba9de" }
    @{ Name = "Rar.solid.rar"; Sha = "ef56f892bf4ed17e1c54bd5f0fce65e3c1a5ec0b" }
    @{ Name = "Rar5.rar"; Sha = "0a05d520923cc46f1daebdddcf6faa89fbafee12" }
    @{ Name = "Rar5.solid.rar"; Sha = "e16a01da81526b480f9d006d73497e1a9656d010" }
    @{ Name = "7Zip.nonsolid.7z"; Sha = "907e1e2c24542ad2d276756dc7d0ecb1f2665eca" }
    @{ Name = "7Zip.solid.7z"; Sha = "5ac695e59aeff2385e9065cb8841118f1bde06e1" }
    @{ Name = "Rar5.encrypted_filesOnly.rar"; Sha = "cb18a2860dfcdb61931a04ea4a5bcdc2e7871705" }
    @{ Name = "7Zip.encryptedFiles.7z"; Sha = "4c15bb2cf8e5407ddc147ffe1c4776c9895253e9" }
    @{ Name = "Rar.multi.part01.rar"; Sha = "c2a44da7b1116f96f9d31a260ab9292a770ce4db" }
    @{ Name = "Zip.deflate.WinzipAES.zip"; Sha = "9325aa8fcb8f5add7cea392bef17b4b3658ed304" }
)
foreach ($fixture in $fixtures) {
    $destination = Join-Path $root $fixture.Name
    $url = "https://raw.githubusercontent.com/adamhathcock/sharpcompress/$commit/tests/TestArchives/Archives/$($fixture.Name)"
    Invoke-WebRequest -Uri $url -OutFile $destination
    $actual = (& git hash-object $destination).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -ne $fixture.Sha) {
        throw "Archive fixture checksum mismatch: $($fixture.Name) expected $($fixture.Sha) got $actual"
    }
}
Write-Host "Verified $($fixtures.Count) archive fixtures at $root."
