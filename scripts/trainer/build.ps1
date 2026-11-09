<#
.SYNOPSIS
    Publish NTech Trainer (Desktop app and CLI) for one or more runtimes.

.DESCRIPTION
    For each runtime in -Runtime (comma separated), publishes the Avalonia app
    (NTechTrainer) and the CLI (ntech-trainer) self-contained into one folder,
    publish\NTechTrainer-<runtime>. The worker's Docker build context and the
    build-image scripts go into its image\ subfolder, so a training machine can
    build the image without the repo.

    -Image cpu|cuda|all also builds the worker image on this machine.
    The version comes from Directory.Build.props unless -Version is given.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\trainer\build.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\trainer\build.ps1 -Runtime win-x64,linux-x64 -Image cpu
#>
[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version,
    [ValidateSet("none", "cpu", "cuda", "all")]
    [string]$Image = "none",
    [string]$OutputRoot,
    [string]$DotnetPath
)

$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "_common.ps1")

$Version = Resolve-Version -Explicit $Version
$dotnet = Resolve-Dotnet -Explicit $DotnetPath
if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot "publish" }

# A value from a .bat arrives as one string, so split it here.
$runtimes = @($Runtime -split '[,;\s]+' | Where-Object { $_ })
if ($runtimes.Count -eq 0) { throw "-Runtime is empty." }

$projects = @(
    (Join-Path $RepoRoot "src\NTech.DataStudio.Trainer.Desktop\NTech.DataStudio.Trainer.Desktop.csproj"),
    (Join-Path $RepoRoot "src\NTech.DataStudio.Trainer.Cli\NTech.DataStudio.Trainer.Cli.csproj")
)

function Copy-ImageContext {
    param([string]$Target)

    $dockerDir = Join-Path $Target "docker\trainer"
    $pythonDir = Join-Path $Target "trainer\python"
    New-Item -ItemType Directory -Force -Path $dockerDir, $pythonDir | Out-Null

    $source = Join-Path $RepoRoot "docker\trainer"
    Copy-Item -LiteralPath (Join-Path $source "Dockerfile"), (Join-Path $source "Dockerfile.dockerignore") -Destination $dockerDir
    Get-ChildItem -LiteralPath $source -Filter "lock-*.txt" | Copy-Item -Destination $dockerDir

    # Same files the image gets: the worker package without bytecode caches.
    Copy-Item -LiteralPath (Join-Path $RepoRoot "trainer\python\ntech_worker") -Destination $pythonDir -Recurse
    Get-ChildItem -LiteralPath $pythonDir -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force

    foreach ($name in @("build-image.ps1", "build-image.sh", "build-image.bat")) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $Target
    }
}

foreach ($rid in $runtimes) {
    $outputDir = Join-Path $OutputRoot "NTechTrainer-$rid"
    $exe = if ($rid -like "win-*") { ".exe" } else { "" }

    Write-Host "==> Publishing NTech Trainer $Version for $rid" -ForegroundColor Cyan
    Write-Host "    Configuration: $Configuration"
    Write-Host "    Output       : $outputDir"
    Write-Host "    dotnet       : $dotnet"

    # A stale file from an older publish must never end up in a package.
    if (Test-Path -LiteralPath $outputDir) {
        Remove-Item -LiteralPath $outputDir -Recurse -Force
    }

    foreach ($project in $projects) {
        & $dotnet publish $project `
            -c $Configuration `
            -r $rid `
            --self-contained true `
            -p:PublishSingleFile=false `
            -p:Version=$Version `
            -o $outputDir `
            --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $project ($rid) with exit code $LASTEXITCODE"
        }
    }

    Copy-ImageContext -Target (Join-Path $outputDir "image")
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "package-README.md") -Destination (Join-Path $outputDir "README.md")

    # The app starts without some of these and fails later, so check them here.
    $native = if ($exe) { @("libSkiaSharp.dll", "libHarfBuzzSharp.dll") } else { @("libSkiaSharp.so", "libHarfBuzzSharp.so") }
    $required = @(
        "NTechTrainer$exe",
        "ntech-trainer$exe",
        "catalog\ultralytics-detect.yaml",
        "catalog\ultralytics-segment.yaml",
        "image\docker\trainer\Dockerfile",
        "image\docker\trainer\lock-cpu.txt",
        "image\docker\trainer\lock-cuda.txt",
        "image\trainer\python\ntech_worker\__main__.py",
        "image\build-image.sh",
        "README.md"
    ) + $native
    foreach ($name in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $outputDir $name))) {
            throw "Publish finished but '$name' is missing from $outputDir."
        }
    }

    Write-Host "==> Published: $outputDir" -ForegroundColor Green
}

$computes = switch ($Image) {
    "none" { @() }
    "all" { @("cpu", "cuda") }
    default { @($Image) }
}
foreach ($compute in $computes) {
    & (Join-Path $PSScriptRoot "build-image.ps1") -Compute $compute
}
