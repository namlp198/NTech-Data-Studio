"""Generates a small synthetic wire harness dataset, to exercise the pipeline before real labels exist.

The objects are drawn, not real: metrics from this dataset say nothing about real inspection quality.
data.yaml carries ``ntech_synthetic: true`` so every report built on it says so.

Without --backgrounds the images use plain noise. --backgrounds is read only; images found there may
hold real unlabeled objects, which is acceptable for a pipeline test and not for measuring a model.

    python make_smoke_dataset.py --out <folder> [--backgrounds <folder>] [--train 60] [--val 20] [--seed 7]
"""

from __future__ import annotations

import argparse
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

NAMES = ["dau_cos", "day_thit", "bang_dinh"]
SIZE = (640, 360)
SUFFIXES = {".jpg", ".jpeg", ".png", ".bmp"}


def _background(rng: random.Random, files: list[Path]) -> Image.Image:
    if files:
        with Image.open(rng.choice(files)) as source:
            return source.convert("RGB").resize(SIZE, Image.BILINEAR)
    noise = np.random.default_rng(rng.randrange(1 << 31)).integers(60, 150, (SIZE[1], SIZE[0], 3), dtype=np.uint8)
    return Image.fromarray(noise, "RGB").filter(ImageFilter.GaussianBlur(3))


def _connector(rng: random.Random) -> Image.Image:
    """dau_cos: a metal crimp terminal, a bright barrel with a darker ring."""
    width, height = rng.randint(40, 80), rng.randint(22, 36)
    patch = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(patch)
    draw.rectangle((0, 0, width - 1, height - 1), fill=(205, 190, 120, 255), outline=(90, 80, 40, 255), width=2)
    draw.ellipse((width * 0.55, 3, width - 4, height - 4), fill=(60, 55, 40, 255))
    return patch


def _cable_tie(rng: random.Random) -> Image.Image:
    """day_thit: a long thin strip with a square head."""
    width, height = rng.randint(70, 130), rng.randint(8, 12)
    colour = rng.choice([(235, 235, 230, 255), (25, 25, 28, 255)])
    patch = Image.new("RGBA", (width, height + 8), (0, 0, 0, 0))
    draw = ImageDraw.Draw(patch)
    draw.rectangle((0, 4, width - 1, height + 3), fill=colour)
    draw.rectangle((0, 0, 16, height + 7), fill=colour, outline=(120, 120, 120, 255))
    return patch


def _tape(rng: random.Random) -> Image.Image:
    """bang_dinh: a wide dark band with faint diagonal stripes."""
    width, height = rng.randint(50, 90), rng.randint(34, 60)
    patch = Image.new("RGBA", (width, height), (35, 35, 40, 255))
    draw = ImageDraw.Draw(patch)
    for offset in range(-height, width, 9):
        draw.line((offset, 0, offset + height, height), fill=(60, 60, 68, 255), width=2)
    return patch


DRAWERS = [_connector, _cable_tie, _tape]


def _make_image(rng: random.Random, files: list[Path]) -> tuple[Image.Image, list[str]]:
    canvas = _background(rng, files)
    lines: list[str] = []
    for _ in range(rng.randint(2, 6)):
        class_id = rng.randrange(len(DRAWERS))
        patch = DRAWERS[class_id](rng).rotate(rng.uniform(-35, 35), expand=True, resample=Image.BICUBIC)
        if patch.width >= SIZE[0] or patch.height >= SIZE[1]:
            continue
        left, top = rng.randint(0, SIZE[0] - patch.width), rng.randint(0, SIZE[1] - patch.height)
        canvas.paste(patch, (left, top), patch)
        bbox = patch.getbbox()
        if bbox is None:
            continue
        x1, y1, x2, y2 = left + bbox[0], top + bbox[1], left + bbox[2], top + bbox[3]
        lines.append(
            f"{class_id} {(x1 + x2) / 2 / SIZE[0]:.6f} {(y1 + y2) / 2 / SIZE[1]:.6f} "
            f"{(x2 - x1) / SIZE[0]:.6f} {(y2 - y1) / SIZE[1]:.6f}"
        )
    return canvas, lines


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--backgrounds", type=Path)
    parser.add_argument("--train", type=int, default=60)
    parser.add_argument("--val", type=int, default=20)
    parser.add_argument("--seed", type=int, default=7)
    args = parser.parse_args()

    if args.out.exists() and any(args.out.iterdir()):
        raise SystemExit(f"{args.out} is not empty; choose a new folder")
    rng = random.Random(args.seed)
    files = sorted(p for p in args.backgrounds.iterdir() if p.suffix.lower() in SUFFIXES) if args.backgrounds else []

    for split, count in (("train", args.train), ("val", args.val)):
        (args.out / "images" / split).mkdir(parents=True)
        (args.out / "labels" / split).mkdir(parents=True)
        for index in range(count):
            image, lines = _make_image(rng, files)
            image.save(args.out / "images" / split / f"synthetic_{index:04d}.jpg", quality=92)
            text = "\n".join(lines) + ("\n" if lines else "")
            (args.out / "labels" / split / f"synthetic_{index:04d}.txt").write_text(text, encoding="utf-8")

    names = "\n".join(f"  {index}: {name}" for index, name in enumerate(NAMES))
    (args.out / "data.yaml").write_text(
        "# Synthetic dataset from trainer/python/tools/make_smoke_dataset.py. Not real inspection data.\n"
        f"ntech_synthetic: true\ntrain: images/train\nval: images/val\nnames:\n{names}\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
