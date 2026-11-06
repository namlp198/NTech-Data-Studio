@echo off
setlocal

rem Publish and build the Inno Setup installer into dist\.
rem Extra arguments are passed to package.ps1, for example:  scripts\package.bat -Version 0.2.0
set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%POWERSHELL_EXE%" set "POWERSHELL_EXE=powershell.exe"

echo ==^> Building the NTech Data Studio installer
"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%package.ps1" %*
if errorlevel 1 (
    echo ==^> FAILED with exit code %errorlevel%
    exit /b %errorlevel%
)

echo ==^> Installer complete
exit /b 0