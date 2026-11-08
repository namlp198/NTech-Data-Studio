#!/usr/bin/env bash
# Publish NTech Trainer (Desktop app and CLI) for one or more runtimes.
#
# For each runtime, publishes the Avalonia app (NTechTrainer) and the CLI
# (ntech-trainer) self-contained into publish/NTechTrainer-<runtime>, with the
# worker's Docker build context and build-image scripts in its image/ folder.
# The version comes from Directory.Build.props unless --version is given.
#
#   scripts/trainer/build.sh
#   scripts/trainer/build.sh --runtime linux-x64,win-x64 --image cpu
#
# Options: --runtime <rid[,rid]> (default linux-x64), --configuration <c>,
#          --version <x.y.z>, --image none|cpu|cuda|all, --output-root <dir>
# dotnet is taken from $DOTNET, then PATH, then ~/.dotnet/dotnet.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
. "$here/_common.sh"

runtime=linux-x64
configuration=Release
version=
image=none
output_root="$repo/publish"

while [ $# -gt 0 ]; do
    case "$1" in
        --runtime) runtime="${2:?--runtime needs a value}"; shift 2 ;;
        --configuration) configuration="${2:?--configuration needs a value}"; shift 2 ;;
        --version) version="${2:?--version needs a value}"; shift 2 ;;
        --image) image="${2:?--image needs a value}"; shift 2 ;;
        --output-root) output_root="${2:?--output-root needs a value}"; shift 2 ;;
        -h|--help) usage "$0"; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage "$0" >&2; exit 2 ;;
    esac
done

case "$image" in
    none) computes=() ;;
    cpu|cuda) computes=("$image") ;;
    all) computes=(cpu cuda) ;;
    *) echo "--image must be none, cpu, cuda or all, got '$image'." >&2; exit 2 ;;
esac

version="$(resolve_version "$repo" "$version")"
dotnet="$(resolve_dotnet)"
mkdir -p "$output_root"
output_root="$(cd "$output_root" && pwd)"

copy_image_context() {
    local target="$1"
    mkdir -p "$target/docker/trainer" "$target/trainer/python"
    cp "$repo/docker/trainer/Dockerfile" "$repo/docker/trainer/Dockerfile.dockerignore" "$repo"/docker/trainer/lock-*.txt \
        "$target/docker/trainer/"
    # Same files the image gets: the worker package without bytecode caches.
    cp -R "$repo/trainer/python/ntech_worker" "$target/trainer/python/"
    find "$target/trainer/python" -type d -name __pycache__ -prune -exec rm -rf {} +
    cp "$here/build-image.sh" "$here/build-image.ps1" "$here/build-image.bat" "$target/"
    chmod +x "$target/build-image.sh"
}

IFS=', ' read -r -a runtimes <<< "$runtime"
for rid in "${runtimes[@]}"; do
    [ -n "$rid" ] || continue
    out="$output_root/NTechTrainer-$rid"
    case "$rid" in win-*) exe=.exe; native=(libSkiaSharp.dll libHarfBuzzSharp.dll) ;;
                   *) exe=; native=(libSkiaSharp.so libHarfBuzzSharp.so) ;; esac

    echo "==> Publishing NTech Trainer $version for $rid"
    echo "    Configuration: $configuration"
    echo "    Output       : $out"
    echo "    dotnet       : $dotnet"

    # A stale file from an older publish must never end up in a package.
    rm -rf "$out"

    for project in \
        "$repo/src/NTech.DataStudio.Trainer.Desktop/NTech.DataStudio.Trainer.Desktop.csproj" \
        "$repo/src/NTech.DataStudio.Trainer.Cli/NTech.DataStudio.Trainer.Cli.csproj"; do
        "$dotnet" publish "$project" \
            -c "$configuration" \
            -r "$rid" \
            --self-contained true \
            -p:PublishSingleFile=false \
            -p:Version="$version" \
            -o "$out" \
            --nologo
    done

    copy_image_context "$out/image"
    cp "$here/package-README.md" "$out/README.md"
    if [ -z "$exe" ]; then chmod +x "$out/NTechTrainer" "$out/ntech-trainer"; fi

    # The app starts without some of these and fails later, so check them here.
    for name in "NTechTrainer$exe" "ntech-trainer$exe" catalog/ultralytics-detect.yaml \
        image/docker/trainer/Dockerfile image/docker/trainer/lock-cpu.txt image/docker/trainer/lock-cuda.txt \
        image/trainer/python/ntech_worker/__main__.py image/build-image.sh README.md "${native[@]}"; do
        if [ ! -e "$out/$name" ]; then
            echo "Publish finished but '$name' is missing from $out." >&2
            exit 1
        fi
    done

    echo "==> Published: $out"
done

for compute in "${computes[@]+"${computes[@]}"}"; do
    "$here/build-image.sh" --compute "$compute"
done
