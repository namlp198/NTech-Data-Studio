<#
.SYNOPSIS
    Build the NTech Data Studio Windows installer with Inno Setup.

.DESCRIPTION
    Publishes the app (via build.ps1 unless -SkipBuild) and runs the Inno Setup 6
    compiler (ISCC.exe) on installer\NTech-DataStudio.iss, producing
    dist\NTech-DataStudio-Setup-<Version>.exe.

    Requires Inno Setup 6 (https://jrsoftware.org/isdl.php). ISCC.exe is located
    via -InnoSetupPath, the ISCC command on PATH, or the default install folders.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\package.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\package.ps1 -Version 0.2.0 -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipBuild,
    [string]$InnoSetupPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$Iss = Join-Path $RepoRoot "installer\NTech-DataStudio.iss"
$PublishDir = Join-Path $RepoRoot "publish\NTechDataStudio"
$DistDir = Join-Path $RepoRoot "dist"
$Version = Resolve-Version -Explicit $Version

# Fail on a missing compiler before spending a minute on the publish.
$iscc = Resolve-Iscc -Explicit $InnoSetupPath

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build.ps1") -Version $Version
}

if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $ExeName))) {
    throw "Publish output not found at $PublishDir. Run build.ps1 first (or omit -SkipBuild)."
}

Write-Host "==> Using Inno Setup compiler: $iscc" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$setup = Join-Path $DistDir "$SetupBaseName-$Version.exe"
if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup -Force }

& $iscc `
    "/Q" `
    "/DMyAppVersion=$Version" `
    "/DSourceDir=$PublishDir" `
    "/O$DistDir" `
    $Iss

if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path -LiteralPath $setup)) {
    throw "ISCC completed but the expected installer was not found at $setup."
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $setup).Length / 1MB, 1)
Write-Host "==> Installer built: $setup ($sizeMb MB)" -ForegroundColor Green
