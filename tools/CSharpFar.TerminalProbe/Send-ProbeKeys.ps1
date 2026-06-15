param(
    [int]$InitialDelayMilliseconds = 1200,
    [int]$BetweenKeysMilliseconds = 250
)

Add-Type -AssemblyName System.Windows.Forms
Start-Sleep -Milliseconds $InitialDelayMilliseconds

$keys = @(
    "a",
    "b",
    "{UP}",
    "{DOWN}",
    "{LEFT}",
    "{RIGHT}",
    "{TAB}",
    "{ENTER}",
    "{ESC}"
)

foreach ($key in $keys) {
    [System.Windows.Forms.SendKeys]::SendWait($key)
    Start-Sleep -Milliseconds $BetweenKeysMilliseconds
}
