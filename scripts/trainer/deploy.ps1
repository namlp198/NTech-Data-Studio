<#
.SYNOPSIS
    Test, publish and package NTech Trainer, and optionally deliver the packages.

.DESCRIPTION
    1. Runs the Trainer tests in Release (NTech.DataStudio.Trainer.slnf) and the
       worker's Python tests when uv is on PATH. Skip both with -SkipTests.
    2. Publishes each runtime through build.ps1.
    3. win-x64 becomes the Inno Setup installer dist\NTech-Trainer-Setup-<version>.exe
       (package.ps1); Linux runtimes become dist\NTechTrainer-<version>-<runtime>.tar.gz
       with the executable bit set on the programs.
    4. With -SaveImage cpu|cuda|all, builds the worker image and saves it as
       dist\ntech-trainer-<compute>-<version>.tar.gz for machines without
       internet (docker load -i <file>).
    5. Writes a .sha256 next to each file.
    6. With -Destination, copies everything there and checks the copies against
       the hashes. An existing file with the same name but different content is
       never overwritten unless -Force is given.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\trainer\deploy.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\scripts\trainer\deploy.ps1 -Runtime win-x64,linux-x64 -SaveImage cuda -Destination \\server\releases\Trainer
#>
[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$Version,
    [ValidateSet("none", "cpu", "cuda", "all")]
    [string]$SaveImage = "none",
    [string]$Destination,
    [switch]$SkipTests,
    [switch]$Force,
    [string]$InnoSetupPath,
    [string]$DotnetPath
)

$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "_common.ps1")

$Version = Resolve-Version -Explicit $Version
$dotnet = Resolve-Dotnet -Explicit $DotnetPath
$runtimes = @($Runtime -split '[,;\s]+' | Where-Object { $_ })
$publishRoot = Join-Path $RepoRoot "publish"
$distDir = Join-Path $RepoRoot "dist"
$computes = switch ($SaveImage) {
    "none" { @() }
    "all" { @("cpu", "cuda") }
    default { @($SaveImage) }
}

# Fail before the long steps when a tool is missing.
$otherWin = @($runtimes | Where-Object { $_ -like "win-*" -and $_ -ne "win-x64" })
if ($otherWin.Count -gt 0) {
    throw "The installer is x64 only; $($otherWin -join ', ') is not supported."
}
if ($runtimes -contains "win-x64") { $null = Resolve-Iscc -Explicit $InnoSetupPath }
$bsdtar = Join-Path $env:SystemRoot "System32\tar.exe"
if (($runtimes | Where-Object { $_ -notlike "win-*" }) -and -not (Test-Path -LiteralPath $bsdtar)) {
    throw "$bsdtar not found; it is needed to pack Linux runtimes (Windows 10 1803 or later)."
}
if ($computes.Count -gt 0 -and -not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "-SaveImage needs docker on PATH."
}

if (-not $SkipTests) {
    Write-Host "==> Running Trainer tests" -ForegroundColor Cyan
    & $dotnet test (Join-Path $RepoRoot "NTech.DataStudio.Trainer.slnf") -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed (exit code $LASTEXITCODE). Nothing was packaged."
    }

    if (Get-Command uv -ErrorAction SilentlyContinue) {
        Write-Host "==> Running worker tests" -ForegroundColor Cyan
        $previous = $env:PYTHONPATH
        $env:PYTHONPATH = Join-Path $RepoRoot "trainer\python"
        try {
            & uv run --no-project --python 3.12 --with pytest --with pillow --with pyyaml --with psutil --with numpy --with opencv-python-headless `
                python -m pytest (Join-Path $RepoRoot "trainer\python\tests") -q
            if ($LASTEXITCODE -ne 0) { throw "Worker tests failed (exit code $LASTEXITCODE). Nothing was packaged." }
        }
        finally { $env:PYTHONPATH = $previous }
    }
    else {
        Write-Warning "uv not found on PATH: the worker's Python tests were NOT run."
    }
}

& (Join-Path $PSScriptRoot "build.ps1") -Runtime ($runtimes -join ",") -Version $Version -DotnetPath $dotnet -OutputRoot $publishRoot

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$outputs = New-Object System.Collections.Generic.List[string]

# mtree escapes every byte outside a safe set as \ooo (octal), like vis(3).
function ConvertTo-MtreePath {
    param([string]$Path)
    $sb = New-Object System.Text.StringBuilder
    foreach ($b in [Text.Encoding]::UTF8.GetBytes($Path)) {
        $c = [char]$b
        if (($c -match '[A-Za-z0-9./_+-]') -and $b -lt 128) { [void]$sb.Append($c) }
        else { [void]$sb.Append('\' + [Convert]::ToString($b, 8).PadLeft(3, '0')) }
    }
    return $sb.ToString()
}

# Windows tar cannot set file modes from NTFS, so describe every entry in an
# mtree manifest: programs 0755, everything else 0644, owner root.
function New-TarGz {
    param([string]$SourceDir, [string]$Archive)

    $parent = Split-Path -Parent $SourceDir
    $executables = @("NTechTrainer", "ntech-trainer", "createdump")
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("#mtree")
    $lines.Add("./$(ConvertTo-MtreePath (Split-Path -Leaf $SourceDir)) type=dir mode=0755 uid=0 gid=0")
    Get-ChildItem -LiteralPath $SourceDir -Recurse -Force | Sort-Object FullName | ForEach-Object {
        $rel = ConvertTo-MtreePath ($_.FullName.Substring($parent.Length + 1).Replace('\', '/'))
        if ($_.PSIsContainer) {
            $lines.Add("./$rel type=dir mode=0755 uid=0 gid=0")
        }
        else {
            $mode = if ($executables -contains $_.Name -or $_.Extension -eq ".sh") { "0755" } else { "0644" }
            $lines.Add("./$rel type=file mode=$mode uid=0 gid=0 contents=$rel")
        }
    }

    $manifest = Join-Path ([IO.Path]::GetTempPath()) "ntech-trainer-$([guid]::NewGuid().ToString('N')).mtree"
    [IO.File]::WriteAllLines($manifest, $lines, [Text.UTF8Encoding]::new($false))
    Push-Location -LiteralPath $parent
    try {
        & $bsdtar -czf $Archive "@$manifest"
        if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE for $Archive" }
    }
    finally {
        Pop-Location
        Remove-Item -LiteralPath $manifest -Force
    }
}

Add-Type -AssemblyName System.IO.Compression

foreach ($rid in $runtimes) {
    if ($rid -eq "win-x64") {
        $packageArgs = @{ Version = $Version; SkipBuild = $true }
        if ($InnoSetupPath) { $packageArgs.InnoSetupPath = $InnoSetupPath }
        & (Join-Path $PSScriptRoot "package.ps1") @packageArgs
        $outputs.Add((Join-Path $distDir "NTech-Trainer-Setup-$Version.exe"))
        continue
    }

    $source = Join-Path $publishRoot "NTechTrainer-$rid"
    $baseName = "NTechTrainer-$Version-$rid"

    # The folder inside the archive carries the version, so unpacking two
    # versions side by side never mixes their files.
    $staging = Join-Path $publishRoot "package\$baseName"
    if (Test-Path -LiteralPath (Split-Path -Parent $staging)) { Remove-Item -LiteralPath (Split-Path -Parent $staging) -Recurse -Force }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $staging) | Out-Null
    Copy-Item -LiteralPath $source -Destination $staging -Recurse

    $archive = Join-Path $distDir "$baseName.tar.gz"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Write-Host "==> Packing $archive" -ForegroundColor Cyan
    New-TarGz -SourceDir $staging -Archive $archive

    Remove-Item -LiteralPath (Split-Path -Parent $staging) -Recurse -Force
    $outputs.Add($archive)
}

foreach ($compute in $computes) {
    # Build first so the saved image always matches these sources.
    & (Join-Path $PSScriptRoot "build-image.ps1") -Compute $compute

    $tag = "ntech-trainer:dev-$compute"
    $tar = Join-Path $distDir "ntech-trainer-$compute-$Version.tar"
    $gz = "$tar.gz"
    Write-Host "==> Saving $tag to $gz" -ForegroundColor Cyan
    & docker save -o $tar $tag
    if ($LASTEXITCODE -ne 0) { throw "docker save failed with exit code $LASTEXITCODE" }

    # PowerShell pipes mangle binary data, so compress with a stream instead.
    $in = [IO.File]::OpenRead($tar)
    try {
        $out = [IO.File]::Create($gz)
        try {
            $zip = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Fastest)
            try { $in.CopyTo($zip, 1MB) } finally { $zip.Dispose() }
        }
        finally { $out.Dispose() }
    }
    finally { $in.Dispose() }
    Remove-Item -LiteralPath $tar -Force
    $outputs.Add($gz)
}

# "<hash> *<file>" is the format `sha256sum -c` reads.
$hashes = @{}
foreach ($file in $outputs) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$file.sha256", "$hash *$(Split-Path -Leaf $file)`n", [Text.UTF8Encoding]::new($false))
    $hashes[$file] = $hash
    $sizeMb = [math]::Round((Get-Item -LiteralPath $file).Length / 1MB, 1)
    Write-Host "    $(Split-Path -Leaf $file)  $sizeMb MB  sha256 $hash"
}

if ($Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    # Check every target before copying any, so a refusal leaves nothing half delivered.
    foreach ($file in $outputs) {
        $target = Join-Path $Destination (Split-Path -Leaf $file)
        if ((Test-Path -LiteralPath $target) -and -not $Force) {
            $existing = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($existing -ne $hashes[$file]) {
                throw "$target already exists with different content. Bump the version, or pass -Force to replace it."
            }
        }
    }

    foreach ($file in $outputs) {
        $target = Join-Path $Destination (Split-Path -Leaf $file)
        Copy-Item -LiteralPath $file -Destination $target -Force
        Copy-Item -LiteralPath "$file.sha256" -Destination "$target.sha256" -Force
        $copied = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($copied -ne $hashes[$file]) {
            throw "The copy at $target does not match (hash $copied). Remove it and retry."
        }
        Write-Host "==> Delivered: $target" -ForegroundColor Green
    }
}
else {
    Write-Host "==> Packages ready in $distDir" -ForegroundColor Green
}
