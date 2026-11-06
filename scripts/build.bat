@echo off
setlocal

rem Publish a Release build into publish\NTechDataStudio.
rem Extra arguments are passed to build.ps1, for example:  scripts\build.bat -Version 0.2.0
set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%POWERSHELL_EXE%" set "POWERSHELL_EXE=powershell.exe"

echo ==^> Publishing NTech Data Studio (Release)
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build.ps1" %*
if errorlevel 1 (
    echo ==^> FAILED with exit code %errorlevel%
    exit /b %errorlevel%
)

echo ==^> Publish complete
exit /b 0