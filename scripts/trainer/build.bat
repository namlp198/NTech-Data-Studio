@echo off
setlocal

rem Publish NTech Trainer (Desktop app and CLI) into publish\NTechTrainer-<runtime>.
rem Extra arguments are passed to build.ps1, for example:
rem   scripts\trainer\build.bat -Runtime win-x64,linux-x64 -Image cpu
set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%POWERSHELL_EXE%" set "POWERSHELL_EXE=powershell.exe"

echo ==^> Publishing NTech Trainer (Release)
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build.ps1" %*
if errorlevel 1 (
    echo ==^> FAILED with exit code %errorlevel%
    exit /b %errorlevel%
)

echo ==^> Publish complete
exit /b 0
