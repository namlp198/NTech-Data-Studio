"""Dataset validation. The layout helper writes data.yaml the way Data Studio exports it (decision 0008)."""

from __future__ import annotations

import hashlib
from pathlib import Path

import pytest
import yaml
from PIL import Image

from ntech_worker import dataset

DATA_STUDIO_YAML = """# NTech Data Studio. Relative paths resolve from the folder this file is in.
train: images/train
val: images/val
names:
  0: "dau_cos"
  1: "day_thit"
  2: "bang_dinh"
"""


def add_image(root: Path, split: str, name: str, label: str | None, colour: tuple[int, int, int] = (10, 20, 30)) -> None:
    (root / "images" / split).mkdir(parents=True, exist_ok=True)
    (root / "labels" / split).mkdir(parents=True, exist_ok=True)
    Image.new("RGB", (640, 360), colour).save(root / "images" / split / f"{name}.png")
    if label is not None:
        (root / "labels" / split / f"{name}.txt").write_text(label, encoding="utf-8")


@pytest.fixture
def good(tmp_path: Path) -> Path:
    (tmp_path / "data.yaml").write_text(DATA_STUDIO_YAML, encoding="utf-8")
    add_image(tmp_path, "train", "a", "0 0.5 0.5 0.2 0.2\n", (1, 1, 1))
    add_image(tmp_path, "val", "b", "2 0.5 0.5 0.2 0.2\n", (2, 2, 2))
    return tmp_path


def codes(items: list[dict]) -> set[str]:
    return {item["code"] for item in items}


def test_data_studio_layout_without_path_key_is_accepted(good: Path) -> None:
    report = dataset.validate(good / "data.yaml")
    assert report.errors == []
    assert report.stats["names"] == ["dau_cos", "day_thit", "bang_dinh"]
    assert report.stats["splits"]["train"] == {"images": 1, "boxes": 1}
    assert report.stats["per_class"]["bang_dinh"] == 1


def test_missing_file_is_an_error(tmp_path: Path) -> None:
    assert codes(dataset.validate(tmp_path / "data.yaml").errors) == {"no_data_yaml"}


def test_names_as_list_and_relative_path_key(tmp_path: Path) -> None:
    root = tmp_path / "set"
    add_image(root, "train", "a", "0 0.5 0.5 0.2 0.2\n", (1, 1, 1))
    add_image(root, "val", "b", "0 0.5 0.5 0.2 0.2\n", (2, 2, 2))
    (tmp_path / "data.yaml").write_text("path: set\ntrain: images/train\nval: images/val\nnames: [x]\n", encoding="utf-8")
    assert dataset.validate(tmp_path / "data.yaml").errors == []


def test_absolute_path_from_another_machine_falls_back_to_the_yaml_folder(good: Path) -> None:
    text = "path: D:/nowhere/at/all\n" + DATA_STUDIO_YAML
    (good / "data.yaml").write_text(text, encoding="utf-8")
    assert dataset.validate(good / "data.yaml").errors == []


@pytest.mark.parametrize(
    ("label", "needle"),
    [
        ("5 0.5 0.5 0.2 0.2\n", "id class"),
        ("0 1.5 0.5 0.2 0.2\n", "tọa độ"),
        ("0 0.5 0.5\n", "sai định dạng"),
        ("abc 0.5 0.5 0.2 0.2\n", "sai định dạng"),
    ],
)
def test_bad_label_lines_block_training(good: Path, label: str, needle: str) -> None:
    (good / "labels" / "train" / "a.txt").write_text(label, encoding="utf-8")
    report = dataset.validate(good / "data.yaml")
    assert "bad_labels" in codes(report.errors)
    assert needle in report.errors[0]["message"]


def test_mismatched_nc_blocks(good: Path) -> None:
    (good / "data.yaml").write_text("nc: 2\n" + DATA_STUDIO_YAML, encoding="utf-8")
    assert "nc_mismatch" in codes(dataset.validate(good / "data.yaml").errors)


def test_no_val_blocks(good: Path) -> None:
    (good / "data.yaml").write_text("train: images/train\nnames: [a, b, c]\n", encoding="utf-8")
    assert "no_val" in codes(dataset.validate(good / "data.yaml").errors)


def test_same_image_in_train_and_val_warns_about_leakage(good: Path) -> None:
    add_image(good, "train", "same", None, (9, 9, 9))
    add_image(good, "val", "same", None, (9, 9, 9))
    assert "leakage" in codes(dataset.validate(good / "data.yaml").warnings)


def test_broken_image_and_missing_label_warn_without_blocking(good: Path) -> None:
    (good / "images" / "train" / "broken.png").write_bytes(b"not an image")
    add_image(good, "train", "unlabeled", None, (3, 3, 3))
    report = dataset.validate(good / "data.yaml")
    assert report.errors == []
    assert {"corrupt_images", "missing_labels"} <= codes(report.warnings)


def test_polygon_rows_in_a_detect_dataset_warn(good: Path) -> None:
    (good / "labels" / "train" / "a.txt").write_text("0 0.1 0.1 0.2 0.1 0.2 0.2\n", encoding="utf-8")
    assert "segment_rows" in codes(dataset.validate(good / "data.yaml", task="detect").warnings)


def test_small_objects_depend_on_imgsz(good: Path) -> None:
    (good / "labels" / "train" / "a.txt").write_text("0 0.5 0.5 0.03 0.03\n", encoding="utf-8")
    assert "tiny_objects" in codes(dataset.validate(good / "data.yaml", imgsz=320).warnings)
    assert "tiny_objects" not in codes(dataset.validate(good / "data.yaml", imgsz=1280).warnings)


def test_few_labels_warns_per_class(good: Path) -> None:
    messages = [w["message"] for w in dataset.validate(good / "data.yaml").warnings if w["code"] == "few_labels"]
    assert any("day_thit" in message for message in messages)


def test_synthetic_dataset_always_says_so(good: Path) -> None:
    (good / "data.yaml").write_text("ntech_synthetic: true\n" + DATA_STUDIO_YAML, encoding="utf-8")
    report = dataset.validate(good / "data.yaml")
    assert report.synthetic
    assert "synthetic" in codes(report.warnings)


def test_fingerprint_follows_labels_and_images(good: Path) -> None:
    first = dataset.validate(good / "data.yaml").fingerprint
    assert first == dataset.validate(good / "data.yaml").fingerprint
    (good / "labels" / "train" / "a.txt").write_text("1 0.5 0.5 0.2 0.2\n", encoding="utf-8")
    assert first != dataset.validate(good / "data.yaml").fingerprint


def _tree_hash(root: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(p for p in root.rglob("*") if p.is_file()):
        digest.update(path.relative_to(root).as_posix().encode())
        digest.update(path.read_bytes())
    return digest.hexdigest()


def test_validation_and_resolve_never_write_into_the_dataset(good: Path, tmp_path_factory: pytest.TempPathFactory) -> None:
    before = _tree_hash(good)
    dataset.validate(good / "data.yaml")
    output = tmp_path_factory.mktemp("run") / "data.resolved.yaml"
    dataset.write_resolved(good / "data.yaml", output)
    assert _tree_hash(good) == before


def test_resolved_yaml_points_at_the_dataset_with_names(good: Path, tmp_path_factory: pytest.TempPathFactory) -> None:
    output = tmp_path_factory.mktemp("run") / "data.resolved.yaml"
    dataset.write_resolved(good / "data.yaml", output)
    resolved = yaml.safe_load(output.read_text(encoding="utf-8"))
    assert resolved["path"] == str(good)
    assert resolved["names"] == ["dau_cos", "day_thit", "bang_dinh"]
    assert "ntech_synthetic" not in resolved


SQUARE = "0 0.4 0.4 0.6 0.4 0.6 0.6 0.4 0.6\n"


def polygon_set(root: Path) -> Path:
    (root / "data.yaml").write_text(DATA_STUDIO_YAML, encoding="utf-8")
    add_image(root, "train", "a", SQUARE, (1, 1, 1))
    add_image(root, "val", "b", "2 0.1 0.1 0.3 0.1 0.2 0.3\n", (2, 2, 2))
    return root


def test_a_polygon_dataset_trains_as_segment_and_counts_both_kinds(tmp_path: Path) -> None:
    report = dataset.validate(polygon_set(tmp_path) / "data.yaml", task="segment")
    assert report.errors == []
    assert report.stats["label_rows"] == {"box": 0, "polygon": 2}


def test_box_rows_are_an_error_for_segment_but_fine_for_detect(good: Path) -> None:
    assert "box_rows_in_segment" in codes(dataset.validate(good / "data.yaml", task="segment").errors)
    assert dataset.validate(good / "data.yaml", task="detect").errors == []


def test_a_mixed_dataset_is_refused_for_segment_and_described_without_a_task(tmp_path: Path) -> None:
    root = polygon_set(tmp_path)
    add_image(root, "train", "box", "1 0.5 0.5 0.2 0.2\n", (9, 9, 9))
    assert "box_rows_in_segment" in codes(dataset.validate(root / "data.yaml", task="segment").errors)
    described = dataset.validate(root / "data.yaml")
    assert described.errors == []
    assert "mixed_label_kinds" in codes(described.warnings)


def test_segment_without_any_polygon_says_so(tmp_path: Path) -> None:
    (tmp_path / "data.yaml").write_text(DATA_STUDIO_YAML, encoding="utf-8")
    add_image(tmp_path, "train", "a", "", (1, 1, 1))
    add_image(tmp_path, "val", "b", "", (2, 2, 2))
    assert "no_polygons" in codes(dataset.validate(tmp_path / "data.yaml", task="segment").errors)


def test_a_polygon_needs_three_vertices_and_whole_pairs(good: Path) -> None:
    for broken in ("0 0.1 0.1 0.2 0.1 0.2 0.2 0.3\n", "0 0.1 0.1 0.2 0.1 0.2\n0 0.1 0.1 0.2 0.1 0.2 0.2 0.3 0.3 0.4\n"):
        (good / "labels" / "train" / "a.txt").write_text(broken, encoding="utf-8")
        report = dataset.validate(good / "data.yaml", task="segment")
        # Five numbers read as a box, which is the other kind of mistake: both are caught.
        assert codes(report.errors) & {"bad_labels", "box_rows_in_segment"}


def test_a_small_polygon_is_measured_by_its_bounding_box(tmp_path: Path) -> None:
    root = polygon_set(tmp_path)
    (root / "labels" / "train" / "a.txt").write_text("0 0.5 0.5 0.52 0.5 0.51 0.52\n", encoding="utf-8")
    assert "tiny_objects" in codes(dataset.validate(root / "data.yaml", imgsz=320, task="segment").warnings)
