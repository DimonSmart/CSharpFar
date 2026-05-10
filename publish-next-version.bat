@echo off
setlocal

pushd "%~dp0" || exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File ".\publish-next-version.ps1" %*
set "exitCode=%ERRORLEVEL%"

popd
exit /b %exitCode%
