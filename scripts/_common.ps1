# Shared helpers, dot-sourced by build.ps1, package.ps1 and deploy.ps1.
# Keep this file ASCII: Windows PowerShell 5.1 reads BOM-less files as ANSI.

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:AppName = "NTech Data Studio"
$script:ExeName = "NTech.DataStudio.Desktop.exe"
$script:SetupBaseName = "NTech-DataStudio-Setup"

function Resolve-Dotnet {
    param([string]$Explicit)

    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit -PathType Leaf) { return $Explicit }
        throw "dotnet executable not found at -DotnetPath '$Explicit'."
    }

    $onPath = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    foreach ($candidate in @("${env:ProgramFiles}\dotnet\dotnet.exe", "${env:ProgramFiles(x86)}\dotnet\dotnet.exe")) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate }
    }

    throw "The .NET SDK was not found. Install .NET SDK 10 (see global.json), add dotnet.exe to PATH, or pass -DotnetPath."
}

# One source of truth for the version: <Version> in Directory.Build.props,
# overridable with -Version. It must be numeric because it also becomes the
# assembly version.
function Resolve-Version {
    param([string]$Explicit)

    $version = $Explicit
    if (-not $version) {
        $props = Join-Path $script:RepoRoot "Directory.Build.props"
        $match = [regex]::Match((Get-Content -Raw -LiteralPath $props), "<Version>\s*([^<\s]+)\s*</Version>")
        if (-not $match.Success) { throw "No <Version> in $props. Add one or pass -Version." }
        $version = $match.Groups[1].Value
    }

    if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
        throw "Version '$version' is not numeric (expected 1.2.3 or 1.2.3.4)."
    }

    return $version
}

function Resolve-Iscc {
    param([string]$Explicit)

    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit -PathType Leaf) { return $Explicit }
        throw "ISCC.exe not found at -InnoSetupPath '$Explicit'."
    }

    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate }
    }

    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 or pass -InnoSetupPath. Download: https://jrsoftware.org/isdl.php"
}
