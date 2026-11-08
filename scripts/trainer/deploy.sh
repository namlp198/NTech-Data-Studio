#!/usr/bin/env bash
# Test, publish and package NTech Trainer, and optionally deliver the packages.
#
#  1. Runs the Trainer tests in Release (NTech.DataStudio.Trainer.slnf) and the
#     worker's Python tests when uv is on PATH. Skip both with --skip-tests.
#  2. Publishes each runtime through build.sh.
#  3. Packs dist/NTechTrainer-<version>-<runtime>.tar.gz (Linux runtimes, with
#     the executable bit on the programs) or .zip (Windows runtimes; the Inno
#     Setup installer needs Windows, see deploy.ps1).
#  4. With --save-image cpu|cuda|all, builds the worker image and saves it as
#     dist/ntech-trainer-<compute>-<version>.tar.gz (docker load -i <file>).
#  5. Writes a .sha256 next to each file.
#  6. With --destination, copies everything there and checks the copies. An
#     existing file with different content is kept unless --force is given.
#
#   scripts/trainer/deploy.sh
#   scripts/trainer/deploy.sh --runtime linux-x64 --save-image cuda --destination /srv/releases/trainer
#
# Options: --runtime <rid[,rid]> (default linux-x64), --version <x.y.z>,
#          --save-image none|cpu|cuda|all, --destination <dir>, --skip-tests, --force
# Needs GNU tar. dotnet is taken from $DOTNET, then PATH, then ~/.dotnet/dotnet.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
. "$here/_common.sh"

runtime=linux-x64
version=
save_image=none
destination=
skip_tests=0
force=0

while [ $# -gt 0 ]; do
    case "$1" in
        --runtime) runtime="${2:?--runtime needs a value}"; shift 2 ;;
        --version) version="${2:?--version needs a value}"; shift 2 ;;
        --save-image) save_image="${2:?--save-image needs a value}"; shift 2 ;;
        --destination) destination="${2:?--destination needs a value}"; shift 2 ;;
        --skip-tests) skip_tests=1; shift ;;
        --force) force=1; shift ;;
        -h|--help) usage "$0"; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage "$0" >&2; exit 2 ;;
    esac
done

case "$save_image" in
    none) computes=() ;;
    cpu|cuda) computes=("$save_image") ;;
    all) computes=(cpu cuda) ;;
    *) echo "--save-image must be none, cpu, cuda or all, got '$save_image'." >&2; exit 2 ;;
esac

version="$(resolve_version "$repo" "$version")"
dotnet="$(resolve_dotnet)"
publish_root="$repo/publish"
dist="$repo/dist"
IFS=', ' read -r -a runtimes <<< "$runtime"

# Fail before the long steps when a tool is missing.
if ! tar --version 2>/dev/null | grep -q 'GNU tar'; then
    echo "GNU tar is needed to set file modes in the package." >&2
    exit 1
fi
# A python3 on PATH can be a stub (the Microsoft Store alias), so run it.
zipper=
if command -v zip >/dev/null 2>&1; then zipper=zip
else
    for py in python3 python; do
        if "$py" -c 'import zipfile' >/dev/null 2>&1; then zipper="$py"; break; fi
    done
fi
for rid in "${runtimes[@]}"; do
    case "$rid" in win-*)
        if [ -z "$zipper" ]; then
            echo "Packing $rid needs zip or a working python3." >&2
            exit 1
        fi ;;
    esac
done
if [ ${#computes[@]} -gt 0 ] && ! command -v docker >/dev/null 2>&1; then
    echo "--save-image needs docker on PATH." >&2
    exit 1
fi

if [ "$skip_tests" -eq 0 ]; then
    echo "==> Running Trainer tests"
    "$dotnet" test "$repo/NTech.DataStudio.Trainer.slnf" -c Release --nologo

    if command -v uv >/dev/null 2>&1; then
        echo "==> Running worker tests"
        PYTHONPATH="$repo/trainer/python" uv run --no-project --python 3.12 \
            --with pytest --with pillow --with pyyaml --with psutil --with numpy --with opencv-python-headless \
            python -m pytest "$repo/trainer/python/tests" -q
    else
        echo "WARNING: uv not found on PATH: the worker's Python tests were NOT run." >&2
    fi
fi

DOTNET="$dotnet" "$here/build.sh" --runtime "$runtime" --version "$version" --output-root "$publish_root"

mkdir -p "$dist"
outputs=()

# Three passes so the modes never depend on the file system: folders and
# programs 0755, everything else 0644, owner root.
make_targz() {
    local parent="$1" name="$2" archive="$3" lists
    lists="$(mktemp -d)"
    (
        cd "$parent"
        find "$name" -type d | LC_ALL=C sort > "$lists/dirs"
        find "$name" -type f \( -name NTechTrainer -o -name ntech-trainer -o -name createdump -o -name '*.sh' \) \
            | LC_ALL=C sort > "$lists/execs"
        find "$name" -type f ! \( -name NTechTrainer -o -name ntech-trainer -o -name createdump -o -name '*.sh' \) \
            | LC_ALL=C sort > "$lists/files"
        local opts=(--no-recursion --owner=0 --group=0 --numeric-owner)
        tar -cf "$archive.tmp" "${opts[@]}" --mode=0755 -T "$lists/dirs"
        tar -rf "$archive.tmp" "${opts[@]}" --mode=0644 -T "$lists/files"
        tar -rf "$archive.tmp" "${opts[@]}" --mode=0755 -T "$lists/execs"
    )
    rm -rf "$lists"
    gzip -n -9 -c "$archive.tmp" > "$archive"
    rm -f "$archive.tmp"
}

make_zip() {
    local parent="$1" name="$2" archive="$3"
    if [ "$zipper" = zip ]; then
        (cd "$parent" && zip -qr -X "$archive" "$name")
    else
        (cd "$parent" && "$zipper" -m zipfile -c "$archive" "$name")
    fi
}

for rid in "${runtimes[@]}"; do
    [ -n "$rid" ] || continue
    base="NTechTrainer-$version-$rid"

    # The folder inside the archive carries the version, so unpacking two
    # versions side by side never mixes their files.
    staging="$publish_root/package"
    rm -rf "$staging"
    mkdir -p "$staging"
    cp -R "$publish_root/NTechTrainer-$rid" "$staging/$base"

    case "$rid" in
        win-*) archive="$dist/$base.zip"; rm -f "$archive"; echo "==> Packing $archive"; make_zip "$staging" "$base" "$archive" ;;
        *) archive="$dist/$base.tar.gz"; rm -f "$archive"; echo "==> Packing $archive"; make_targz "$staging" "$base" "$archive" ;;
    esac

    rm -rf "$staging"
    outputs+=("$archive")
done

for compute in "${computes[@]+"${computes[@]}"}"; do
    # Build first so the saved image always matches these sources.
    "$here/build-image.sh" --compute "$compute"
    tag="ntech-trainer:dev-$compute"
    file="$dist/ntech-trainer-$compute-$version.tar.gz"
    echo "==> Saving $tag to $file"
    docker save "$tag" | gzip -1 > "$file"
    outputs+=("$file")
done

# "<hash> *<file>" is the format `sha256sum -c` reads.
declare -A hashes
for file in "${outputs[@]}"; do
    hash="$(sha256_of "$file")"
    printf '%s *%s\n' "$hash" "$(basename "$file")" > "$file.sha256"
    hashes["$file"]="$hash"
    size_mb="$(( ($(wc -c < "$file") + 524288) / 1048576 ))"
    echo "    $(basename "$file")  ${size_mb} MB  sha256 $hash"
done

if [ -n "$destination" ]; then
    mkdir -p "$destination"

    # Check every target before copying any, so a refusal leaves nothing half delivered.
    for file in "${outputs[@]}"; do
        target="$destination/$(basename "$file")"
        if [ -e "$target" ] && [ "$force" -eq 0 ] && [ "$(sha256_of "$target")" != "${hashes[$file]}" ]; then
            echo "$target already exists with different content. Bump the version, or pass --force to replace it." >&2
            exit 1
        fi
    done

    for file in "${outputs[@]}"; do
        target="$destination/$(basename "$file")"
        cp -f "$file" "$target"
        cp -f "$file.sha256" "$target.sha256"
        copied="$(sha256_of "$target")"
        if [ "$copied" != "${hashes[$file]}" ]; then
            echo "The copy at $target does not match (hash $copied). Remove it and retry." >&2
            exit 1
        fi
        echo "==> Delivered: $target"
    done
else
    echo "==> Packages ready in $dist"
fi
