[CmdletBinding()]
param(
    # The initial public v1.0.0 SDK commit. Move this only with a reviewed
    # major-version migration or a deliberate rebaseline.
    [string]$BaselineCommit = '6d9873abc0c855d93325d5227b34fe6f8eabadb8'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$baselineDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "csharpfar-plugin-api-$([Guid]::NewGuid().ToString('N'))"
$worktreeAdded = $false

try {
    & git -C $root worktree add --detach $baselineDirectory $BaselineCommit
    if ($LASTEXITCODE -ne 0) { throw "Cannot prepare SDK API baseline $BaselineCommit." }
    $worktreeAdded = $true

    Push-Location $root
    try {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Cannot restore pinned Microsoft.DotNet.ApiCompat.Tool.' }

        foreach ($project in @('CSharpFar.Core', 'CSharpFar.Plugin.Abstractions')) {
            $relative = "src/$project/$project.csproj"
            & dotnet build (Join-Path $baselineDirectory $relative) -c Release
            if ($LASTEXITCODE -ne 0) { throw "Cannot build baseline $project." }
            & dotnet build (Join-Path $root $relative) -c Release
            if ($LASTEXITCODE -ne 0) { throw "Cannot build current $project." }

            $left = Join-Path $baselineDirectory "src/$project/bin/Release/net10.0/$project.dll"
            $right = Join-Path $root "src/$project/bin/Release/net10.0/$project.dll"
            & dotnet tool run apicompat -- --left $left --right $right --enable-rule-cannot-change-parameter-name
            if ($LASTEXITCODE -ne 0) {
                throw "Breaking public API change detected in $project. Review SDK versioning before releasing."
            }
        }
    }
    finally { Pop-Location }
    Write-Host 'PASS: public plugin SDK binaries remain compatible with the v1.0.0 baseline.'
}
finally {
    if ($worktreeAdded) {
        & git -C $root worktree remove --force $baselineDirectory
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Remove the temporary baseline worktree manually: $baselineDirectory"
        }
    }
}
