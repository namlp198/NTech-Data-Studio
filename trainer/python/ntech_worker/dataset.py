"""Reads and validates a YOLO detect dataset. Never writes inside the dataset folder.

Imports nothing from the training stack, so validation stays fast and testable anywhere.
"""

from __future__ import annotations

import hashlib
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml
from PIL import Image

IMAGE_SUFFIXES = {".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"}
SPLITS = ("train", "val", "test")
FEW_LABELS = 50
IMBALANCE_RATIO = 10.0
MIN_OBJECT_PX = 12


@dataclass
class Report:
    errors: list[dict[str, Any]] = field(default_factory=list)
    warnings: list[dict[str, Any]] = field(default_factory=list)
    stats: dict[str, Any] = field(default_factory=dict)
    fingerprint: str = ""
    synthetic: bool = False

    def error(self, code: str, message: str) -> None:
        self.errors.append({"code": code, "message": message})

    def warn(self, code: str, message: str) -> None:
        self.warnings.append({"code": code, "message": message})

    def as_dict(self) -> dict[str, Any]:
        return {
            "ok": not self.errors,
            "errors": self.errors,
            "warnings": self.warnings,
            "stats": self.stats,
            "fingerprint": self.fingerprint,
            "synthetic": self.synthetic,
        }


def _class_names(raw: Any) -> list[str] | None:
    if isinstance(raw, list):
        return [str(name) for name in raw]
    if isinstance(raw, dict):
        try:
            ids = sorted(int(key) for key in raw)
        except (TypeError, ValueError):
            return None
        if ids != list(range(len(ids))):
            return None
        return [str(raw.get(i, raw.get(str(i)))) for i in ids]
    return None


def dataset_root(data_yaml: Path, doc: dict[str, Any]) -> Path:
    """No ``path`` key means the folder of data.yaml, as Data Studio exports (decision 0008).

    An absolute ``path`` from another machine or from the host cannot exist in the container, and
    the mount is always the folder of data.yaml, so that folder is the answer there too.
    """
    base = data_yaml.parent
    declared = doc.get("path")
    if not declared:
        return base
    candidate = Path(str(declared))
    if not candidate.is_absolute():
        return (base / candidate).resolve()
    return candidate if candidate.is_dir() else base


def _split_dirs(root: Path, value: Any) -> list[Path]:
    items = value if isinstance(value, list) else [value]
    return [root / str(item) for item in items if item]


def _image_files(directory: Path) -> list[Path]:
    if not directory.is_dir():
        return []
    return sorted(p for p in directory.rglob("*") if p.suffix.lower() in IMAGE_SUFFIXES and p.is_file())


def _label_path(image: Path) -> Path:
    """Ultralytics finds a label by replacing the last ``images`` folder with ``labels``."""
    parts = list(image.parts)
    for index in range(len(parts) - 2, -1, -1):
        if parts[index] == "images":
            parts[index] = "labels"
            return Path(*parts).with_suffix(".txt")
    return image.with_suffix(".txt")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def validate(data_yaml: Path, imgsz: int = 640) -> Report:
    report = Report()
    if not data_yaml.is_file():
        report.error("no_data_yaml", f"Không tìm thấy {data_yaml.name}.")
        return report
    try:
        doc = yaml.safe_load(data_yaml.read_text(encoding="utf-8")) or {}
    except (yaml.YAMLError, UnicodeDecodeError) as problem:
        report.error("bad_data_yaml", f"Không đọc được data.yaml: {problem}")
        return report
    if not isinstance(doc, dict):
        report.error("bad_data_yaml", "data.yaml phải là một bảng khóa và giá trị.")
        return report

    report.synthetic = bool(doc.get("ntech_synthetic"))
    if report.synthetic:
        report.warn("synthetic", "Dataset tổng hợp: chỉ dùng để thử luồng, metric không nói lên chất lượng thật.")

    names = _class_names(doc.get("names"))
    if not names:
        report.error("no_names", "data.yaml thiếu names, hoặc id class không liên tục từ 0.")
        return report
    if "nc" in doc and doc["nc"] != len(names):
        report.error("nc_mismatch", f"nc là {doc['nc']} nhưng names có {len(names)} class.")

    root = dataset_root(data_yaml, doc)
    per_class: Counter[int] = Counter()
    split_stats: dict[str, Any] = {}
    fingerprint = hashlib.sha256(data_yaml.read_bytes())
    hashes_by_split: dict[str, set[str]] = {}
    corrupt = missing_labels = seg_rows = tiny = 0
    bad_line_samples: list[str] = []
    bad_lines = 0
    min_sides: list[float] = []

    for split in SPLITS:
        if not doc.get(split):
            continue
        images: list[Path] = []
        for directory in _split_dirs(root, doc[split]):
            images += _image_files(directory)
        hashes_by_split[split] = set()
        boxes = 0
        for image in images:
            size = image.stat().st_size
            fingerprint.update(f"{image.relative_to(root).as_posix()}|{size}\n".encode())
            hashes_by_split[split].add(_sha256(image))
            try:
                with Image.open(image) as handle:
                    handle.verify()
                with Image.open(image) as handle:
                    width, height = handle.size
            except Exception:  # Pillow raises many types for a broken file
                corrupt += 1
                continue
            label = _label_path(image)
            if not label.is_file():
                missing_labels += 1
                continue
            fingerprint.update(label.read_bytes())
            scale = imgsz / max(width, height)
            for number, line in enumerate(label.read_text(encoding="utf-8").splitlines(), start=1):
                if not line.strip():
                    continue
                fields = line.split()
                where = f"{label.name}:{number}"
                try:
                    values = [float(field_) for field_ in fields]
                except ValueError:
                    values = []
                if len(values) < 5 or (len(values) > 5 and len(values) % 2 == 0):
                    bad_lines += 1
                    if len(bad_line_samples) < 5:
                        bad_line_samples.append(f"{where}: sai định dạng")
                    continue
                if len(values) > 5:
                    seg_rows += 1
                class_id = values[0]
                if class_id != int(class_id) or not 0 <= class_id < len(names):
                    bad_lines += 1
                    if len(bad_line_samples) < 5:
                        bad_line_samples.append(f"{where}: id class {fields[0]} ngoài phạm vi")
                    continue
                if not all(0.0 <= value <= 1.0 for value in values[1:]):
                    bad_lines += 1
                    if len(bad_line_samples) < 5:
                        bad_line_samples.append(f"{where}: tọa độ ngoài 0..1")
                    continue
                per_class[int(class_id)] += 1
                boxes += 1
                if len(values) == 5:
                    side = min(values[3] * width, values[4] * height) * scale
                    min_sides.append(side)
                    if side < MIN_OBJECT_PX:
                        tiny += 1
        split_stats[split] = {"images": len(images), "boxes": boxes}

    if not split_stats.get("train", {}).get("images"):
        report.error("no_train", "Không có ảnh train.")
    if not split_stats.get("val", {}).get("images"):
        report.error("no_val", "Không có ảnh val.")
    if bad_lines:
        report.error("bad_labels", f"{bad_lines} dòng nhãn lỗi, ví dụ: " + "; ".join(bad_line_samples))
    if corrupt:
        report.warn("corrupt_images", f"{corrupt} ảnh không đọc được và sẽ bị bỏ qua.")
    if missing_labels:
        report.warn("missing_labels", f"{missing_labels} ảnh không có file nhãn, được coi là không có vật thể.")
    if seg_rows:
        report.warn("segment_rows", f"{seg_rows} dòng nhãn dạng polygon trong dataset detect.")

    for class_id, name in enumerate(names):
        if per_class[class_id] < FEW_LABELS:
            report.warn("few_labels", f"Class {name} chỉ có {per_class[class_id]} nhãn, dưới {FEW_LABELS}.")
    present = [count for count in per_class.values() if count]
    if present and max(present) / min(present) >= IMBALANCE_RATIO:
        report.warn("imbalance", f"Mất cân bằng class: nhiều nhất gấp {max(present) / min(present):.0f} lần ít nhất.")
    if tiny:
        report.warn("tiny_objects", f"{tiny} nhãn nhỏ hơn {MIN_OBJECT_PX} px ở imgsz {imgsz}. Cân nhắc tăng imgsz hoặc chia tile.")

    train_hashes = hashes_by_split.get("train", set())
    for other in ("val", "test"):
        shared = train_hashes & hashes_by_split.get(other, set())
        if shared:
            report.warn("leakage", f"{len(shared)} ảnh giống hệt nhau nằm ở cả train và {other}: metric sẽ cao hơn thực tế.")

    min_sides.sort()
    report.stats = {
        "names": names,
        "splits": split_stats,
        "per_class": {names[i]: per_class[i] for i in range(len(names))},
        "imgsz": imgsz,
        "min_side_px_at_imgsz": (
            {
                "p5": round(min_sides[len(min_sides) // 20], 1),
                "p50": round(min_sides[len(min_sides) // 2], 1),
            }
            if min_sides
            else None
        ),
    }
    report.fingerprint = fingerprint.hexdigest()
    return report


def write_resolved(data_yaml: Path, output: Path) -> dict[str, Any]:
    """Writes data.yaml for this run with an absolute root, leaving the original untouched."""
    doc = yaml.safe_load(data_yaml.read_text(encoding="utf-8"))
    resolved = {
        "path": str(dataset_root(data_yaml, doc)),
        "train": doc.get("train"),
        "val": doc.get("val"),
        "names": _class_names(doc.get("names")),
    }
    if doc.get("test"):
        resolved["test"] = doc["test"]
    output.write_text(yaml.safe_dump(resolved, allow_unicode=True, sort_keys=False), encoding="utf-8")
    return resolved
