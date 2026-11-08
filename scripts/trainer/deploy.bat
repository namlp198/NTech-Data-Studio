@echo off
setlocal

rem Test, publish and package NTech Trainer into dist\, optionally copy it with -Destination.
rem Extra arguments are passed to deploy.ps1, for example:
rem   scripts\trainer\deploy.bat -Runtime win-x64,linux-x64 -SaveImage cuda -Destination \\server\releases\Trainer
set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%POWERSHELL_EXE%" set "POWERSHELL_EXE=powershell.exe"

echo ==^> Deploying NTech Trainer
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%deploy.ps1" %*
if errorlevel 1 (
    echo ==^> FAILED with exit code %errorlevel%
    exit /b %errorlevel%
)

echo ==^> Deploy complete
exit /b 0
