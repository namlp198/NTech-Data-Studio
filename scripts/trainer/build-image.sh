#!/usr/bin/env bash
# Build the NTech Trainer worker image (Docker).
#
# Works from the repo (context = repo root) and from an unpacked Trainer
# package (context = the image folder this script sits in). The default tag is
# ntech-trainer:dev-<compute>, the name JobService.ImageFor looks for.
#
# Needs Docker and internet access (pip downloads PyTorch and Ultralytics).
#
#   ./build-image.sh                 # cpu
#   ./build-image.sh --compute cuda
#   ./build-image.sh --compute cpu --tag my-registry/ntech-trainer:dev-cpu
set -euo pipefail

compute=cpu
tag=

# Kept standalone (no _common.sh): this script is copied into the Trainer package.
usage() { sed -n '2,/^[^#]/p' "$0" | sed '$d' | sed 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
    case "$1" in
        --compute) compute="${2:?--compute needs a value}"; shift 2 ;;
        --tag) tag="${2:?--tag needs a value}"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
    esac
done

case "$compute" in
    cpu) torch_index=https://download.pytorch.org/whl/cpu ;;
    cuda) torch_index=https://download.pytorch.org/whl/cu132 ;;
    *) echo "--compute must be cpu or cuda, got '$compute'." >&2; exit 2 ;;
esac

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
context=
for candidate in "$here" "$(cd "$here/../.." && pwd)"; do
    if [ -f "$candidate/docker/trainer/Dockerfile" ]; then context="$candidate"; break; fi
done
if [ -z "$context" ]; then
    echo "docker/trainer/Dockerfile not found next to this script or at the repo root." >&2
    exit 1
fi

if ! command -v docker >/dev/null 2>&1; then
    echo "docker was not found on PATH. Install Docker Engine (and the NVIDIA Container Toolkit for cuda)." >&2
    exit 1
fi

[ -n "$tag" ] || tag="ntech-trainer:dev-$compute"

echo "==> Building worker image $tag"
echo "    Context    : $context"
echo "    Torch index: $torch_index"

docker build \
    -f "$context/docker/trainer/Dockerfile" \
    --build-arg "COMPUTE=$compute" \
    --build-arg "TORCH_INDEX=$torch_index" \
    -t "$tag" \
    "$context"

echo "==> Image ready: $tag"
