# Regenerates lock-cpu.txt and lock-cuda.txt. Run after changing a requirements file,
# then rebuild both images and rerun the catalog check.
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

function Compile($name, $index) {
    uv pip compile (Join-Path $here "requirements-$name.in") `
        --python-platform x86_64-manylinux_2_28 --python-version 3.12 `
        --index-url $index --extra-index-url https://pypi.org/simple `
        --index-strategy unsafe-best-match --no-header --no-annotate -q `
        -o (Join-Path $here "lock-$name.txt")
    if ($LASTEXITCODE -ne 0) { throw "uv pip compile failed for $name" }
}

Compile 'cpu' 'https://download.pytorch.org/whl/cpu'
Compile 'cuda' 'https://download.pytorch.org/whl/cu132'
