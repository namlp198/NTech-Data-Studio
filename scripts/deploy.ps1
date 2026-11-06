<#
.SYNOPSIS
    Test, publish, package and optionally deliver the NTech Data Studio installer.

.DESCRIPTION
    1. Runs all tests in Release (skip with -SkipTests).
    2. Publishes and builds the installer through package.ps1.
    3. Writes dist\<installer>.sha256.
    4. With -Destination, copies the installer and its .sha256 there and checks
       the copy against the hash. An existing file with the same name but a
       different hash is never overwritten unless -Force is given.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\deploy.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\deploy.ps1 -Version 0.2.0 -Destination \\server\releases\DataStudio
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Destination,
    [switch]$SkipTests,
    [switch]$Force,
    [string]$InnoSetupPath,
    [string]$DotnetPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$Version = Resolve-Version -Explicit $Version
$dotnet = Resolve-Dotnet -Explicit $DotnetPath
$setup = Join-Path $RepoRoot "dist\$SetupBaseName-$Version.exe"
$hashFile = "$setup.sha256"

if (-not $SkipTests) {
    Write-Host "==> Running tests" -ForegroundColor Cyan
    & $dotnet test (Join-Path $RepoRoot "NTech.DataStudio.slnx") -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed (exit code $LASTEXITCODE). Nothing was packaged."
    }
}

$packageArgs = @{ Version = $Version }
if ($InnoSetupPath) { $packageArgs.InnoSetupPath = $InnoSetupPath }
& (Join-Path $PSScriptRoot "package.ps1") @packageArgs

# "<hash> *<file>" is the format `sha256sum -c` reads.
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($hashFile, "$hash *$(Split-Path -Leaf $setup)`n", [Text.UTF8Encoding]::new($false))
Write-Host "==> SHA-256: $hash" -ForegroundColor Cyan

if ($Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $target = Join-Path $Destination (Split-Path -Leaf $setup)

    if ((Test-Path -LiteralPath $target) -and -not $Force) {
        $existing = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existing -ne $hash) {
            throw "$target already exists with different content. Bump the version, or pass -Force to replace it."
        }
    }

    Copy-Item -LiteralPath $setup -Destination $target -Force
    Copy-Item -LiteralPath $hashFile -Destination "$target.sha256" -Force

    $copied = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($copied -ne $hash) {
        throw "The copy at $target does not match the installer (hash $copied). Remove it and retry."
    }
    Write-Host "==> Delivered to: $target" -ForegroundColor Green
}
else {
    Write-Host "==> Installer ready: $setup" -ForegroundColor Green
}
