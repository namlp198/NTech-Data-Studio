<#
.SYNOPSIS
    Build the NTech Trainer worker image (Docker).

.DESCRIPTION
    Works from the repo (context = repo root) and from an unpacked Trainer
    package (context = the image folder this script sits in). The default tag
    is ntech-trainer:dev-<compute>, the name JobService.ImageFor looks for, so
    the app and the CLI find the image without extra settings.

    Needs Docker and internet access (pip downloads PyTorch and Ultralytics).
    The cuda image needs an NVIDIA GPU from Turing (SM 7.5) on to be useful.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-image.ps1 -Compute cpu

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build-image.ps1 -Compute cuda
#>
[CmdletBinding()]
param(
    [ValidateSet("cpu", "cuda")]
    [string]$Compute = "cpu",
    [string]$Tag
)

$ErrorActionPreference = "Stop"
# Keep this file ASCII and standalone: it is copied into the Trainer package.

$candidates = @($PSScriptRoot, (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$context = $candidates | Where-Object { Test-Path -LiteralPath (Join-Path $_ "docker\trainer\Dockerfile") } | Select-Object -First 1
if (-not $context) {
    throw "docker\trainer\Dockerfile not found next to this script or at the repo root."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "docker was not found on PATH. Install Docker Desktop (Windows) or Docker Engine (Linux)."
}

if (-not $Tag) { $Tag = "ntech-trainer:dev-$Compute" }
$torchIndex = @{
    cpu  = "https://download.pytorch.org/whl/cpu"
    cuda = "https://download.pytorch.org/whl/cu132"
}[$Compute]

Write-Host "==> Building worker image $Tag" -ForegroundColor Cyan
Write-Host "    Context    : $context"
Write-Host "    Torch index: $torchIndex"

& docker build `
    -f (Join-Path $context "docker\trainer\Dockerfile") `
    --build-arg "COMPUTE=$Compute" `
    --build-arg "TORCH_INDEX=$torchIndex" `
    -t $Tag `
    $context

if ($LASTEXITCODE -ne 0) {
    throw "docker build failed with exit code $LASTEXITCODE"
}

Write-Host "==> Image ready: $Tag" -ForegroundColor Green
