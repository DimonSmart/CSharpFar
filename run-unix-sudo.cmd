@echo off
setlocal

set "CSHARPFAR_REPO_DIR=%~dp0"
if "%CSHARPFAR_REPO_DIR:~-1%"=="\" set "CSHARPFAR_REPO_DIR=%CSHARPFAR_REPO_DIR:~0,-1%"

if defined CSHARPFAR_WSL_DISTRO (
    wsl.exe -d "%CSHARPFAR_WSL_DISTRO%" -u root --cd "%CSHARPFAR_REPO_DIR%" --exec bash ./run-unix-sudo.sh
) else (
    wsl.exe -u root --cd "%CSHARPFAR_REPO_DIR%" --exec bash ./run-unix-sudo.sh
)

if errorlevel 1 (
    echo.
    echo run-unix-sudo.cmd failed with exit code %errorlevel%.
    pause
)
