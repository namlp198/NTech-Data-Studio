<#
.SYNOPSIS
    Publish a Release build of NTech Data Studio, ready for packaging.

.DESCRIPTION
    Runs `dotnet publish` on the Desktop project into .\publish\NTechDataStudio.
    Defaults to a self-contained win-x64 publish, so the installer does not
    need the .NET Desktop Runtime on the target machine. The version comes from
    Directory.Build.props unless -Version is given.

    For a quick debug build while developing, use:
        dotnet build NTech.DataStudio.slnx

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Version 0.2.0 -SelfContained:$false
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [string]$Version,
    [string]$OutputDir,
    [string]$DotnetPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_common.ps1")

$Project = Join-Path $RepoRoot "src\NTech.DataStudio.Desktop\NTech.DataStudio.Desktop.csproj"
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot "publish\NTechDataStudio" }
$Version = Resolve-Version -Explicit $Version
$dotnet = Resolve-Dotnet -Explicit $DotnetPath

Write-Host "==> Publishing $AppName $Version" -ForegroundColor Cyan
Write-Host "    Project      : $Project"
Write-Host "    Configuration: $Configuration"
Write-Host "    Runtime      : $Runtime"
Write-Host "    SelfContained: $SelfContained"
Write-Host "    Output       : $OutputDir"
Write-Host "    dotnet       : $dotnet"

# A stale file from an older publish must never end up in the installer.
if (Test-Path -LiteralPath $OutputDir) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}

$selfContainedArg = if ($SelfContained) { "true" } else { "false" }
& $dotnet publish $Project `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedArg `
    -p:PublishSingleFile=false `
    -p:Version=$Version `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# The app starts fine without these and then fails on first use, so check them here.
$required = @(
    $ExeName,
    "OpenCvSharpExtern.dll",
    "onnxruntime.dll",
    "profiles\yolo26-detect.yaml"
)
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $OutputDir $name))) {
        throw "Publish finished but '$name' is missing from $OutputDir."
    }
}

Write-Host "==> Publish complete: $OutputDir" -ForegroundColor Green
