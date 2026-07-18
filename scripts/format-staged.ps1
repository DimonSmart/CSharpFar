$ErrorActionPreference = "Stop"

$solution = "CSharpFar.slnx"

$stagedFiles = git diff --cached --name-only --diff-filter=ACMR |
    Where-Object { $_ -match '\.(cs|csproj|props|targets|sln|slnx|editorconfig)$' }

if (-not $stagedFiles -or $stagedFiles.Count -eq 0) {
    Write-Host "No staged .NET files to format."
    exit 0
}

Write-Host "Formatting staged .NET files..."

$formatInput = @()
foreach ($file in $stagedFiles) {
    if (Test-Path $file -PathType Leaf) {
        $formatInput += "--include"
        $formatInput += $file
    }
}

if ($formatInput.Count -eq 0) {
    Write-Host "No existing staged files to format."
    exit 0
}

dotnet format $solution --no-restore @formatInput

foreach ($file in $stagedFiles) {
    if (Test-Path $file -PathType Leaf) {
        git add $file
    }
}

Write-Host "Formatting completed."
