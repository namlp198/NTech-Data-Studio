<#
.SYNOPSIS
    Build the NTech Trainer Windows installer with Inno Setup.

.DESCRIPTION
    Publishes win-x64 (via build.ps1 unless -SkipBuild) and runs the Inno Setup 6
    compiler (ISCC.exe) on trainer\installer\NTech-Trainer.iss, producing
    dist\NTech-Trainer-Setup-<Version>.exe.

    Windows only: there is no Inno Setup on Linux, so deploy.sh packs a zip for
    Windows runtimes instead.

    Requires Inno Setup 6 (https://jrsoftware.org/isdl.php). ISCC.exe is located
    via -InnoSetupPath, the ISCC command on PATH, or the default install folders.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\trainer\package.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\trainer\package.ps1 -Version 0.2.0 -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipBuild,
    [string]$InnoSetupPath,
    [string]$DotnetPath
)

$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "_common.ps1")

$Iss = Join-Path $RepoRoot "trainer\installer\NTech-Trainer.iss"
$PublishDir = Join-Path $RepoRoot "publish\NTechTrainer-win-x64"
$DistDir = Join-Path $RepoRoot "dist"
$Version = Resolve-Version -Explicit $Version

# Fail on a missing compiler before spending a minute on the publish.
$iscc = Resolve-Iscc -Explicit $InnoSetupPath

if (-not $SkipBuild) {
    $buildArgs = @{ Runtime = "win-x64"; Version = $Version }
    if ($DotnetPath) { $buildArgs.DotnetPath = $DotnetPath }
    & (Join-Path $PSScriptRoot "build.ps1") @buildArgs
}

if (-not (Test-Path -LiteralPath (Join-Path $PublishDir "NTechTrainer.exe"))) {
    throw "Publish output not found at $PublishDir. Run build.ps1 first (or omit -SkipBuild)."
}

Write-Host "==> Using Inno Setup compiler: $iscc" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

$setup = Join-Path $DistDir "NTech-Trainer-Setup-$Version.exe"
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
