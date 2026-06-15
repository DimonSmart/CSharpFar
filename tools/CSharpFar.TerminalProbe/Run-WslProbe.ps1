param(
    [string]$Distribution = "Ubuntu-24.04",
    [ValidateSet("raw", "console")]
    [string]$Mode = "raw",
    [string]$LogPath = ""
)

$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ($root -notmatch '^([A-Za-z]):\\(.*)$') {
    throw "Expected an absolute Windows path, got: $root"
}

$drive = $Matches[1].ToLowerInvariant()
$tail = $Matches[2] -replace '\\', '/'
$wslRoot = "/mnt/$drive/$tail"
$logArgument = ""
if ($LogPath) {
    $absoluteLogPath = (New-Item -ItemType File -Force -Path $LogPath).FullName
    if ($absoluteLogPath -notmatch '^([A-Za-z]):\\(.*)$') {
        throw "Expected an absolute Windows log path, got: $absoluteLogPath"
    }

    $logDrive = $Matches[1].ToLowerInvariant()
    $logTail = $Matches[2] -replace '\\', '/'
    $wslLogPath = "/mnt/$logDrive/$logTail"
    $logArgument = " --log '$wslLogPath'"
}

$command = "cd '$wslRoot' && dotnet run --project tools/CSharpFar.TerminalProbe -- --$Mode$logArgument"

if (Get-Command wt.exe -ErrorAction SilentlyContinue) {
    Start-Process wt.exe -ArgumentList @(
        "new-tab",
        "--title", "CSharpFar TerminalProbe",
        "wsl.exe", "-d", $Distribution, "-e", "bash", "-lc", "$command; echo; read -r -p 'Press Enter to close...'"
    )
    return
}

wsl.exe -d $Distribution -e bash -lc $command
