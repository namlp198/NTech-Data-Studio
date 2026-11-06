@echo off
setlocal

rem Run tests, build the installer, write its checksum and optionally copy it with -Destination.
rem Extra arguments are passed to deploy.ps1, for example:  scripts\deploy.bat -Destination \\server\releases -Version 0.2.0
set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%POWERSHELL_EXE%" set "POWERSHELL_EXE=powershell.exe"

echo ==^> Deploying NTech Data Studio
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%deploy.ps1" %*
if errorlevel 1 (
    echo ==^> FAILED with exit code %errorlevel%
    exit /b %errorlevel%
)

echo ==^> Deploy complete
exit /b 0