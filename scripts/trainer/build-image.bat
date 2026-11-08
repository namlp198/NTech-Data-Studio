@echo off
setlocal

rem Build the NTech Trainer worker image. Extra arguments are passed to build-image.ps1, for example:
rem   build-image.bat -Compute cuda
set "SCRIPT_DIR=%~dp0"
set "POWERSHELL_EXE=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if not exist "%POWERSHELL_EXE%" set "POWERSHELL_EXE=powershell.exe"

"%POWERSHELL_EXE%" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%build-image.ps1" %*
if errorlevel 1 (
    echo ==^> FAILED with exit code %errorlevel%
    exit /b %errorlevel%
)
exit /b 0
