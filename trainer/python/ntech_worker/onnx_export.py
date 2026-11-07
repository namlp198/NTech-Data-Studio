"""Exports best.pt to ONNX and checks the result against PyTorch.

The check does not go through the Ultralytics predictor for ONNX: it preprocesses and decodes the way
model.meta.json tells a consumer to, so the metadata is tested along with the model.
"""

from __future__ import annotations

import json
import os
import shutil
import statistics
import time
from pathlib import Path
from typing import Any

import numpy as np

from .dataset import IMAGE_SUFFIXES, _split_dirs, dataset_root
from .events import EventLog, finite
from .runtime import RunPaths, write_json
from .ultralytics_adapter import _prepare

COMPARE_CONF = 0.001  # the threshold metrics are computed at: every box that counts toward mAP is compared
IOU = 0.7
MAX_DET = 300
MAX_IMAGES = 20
MIN_BOX_IOU = 0.99
MAX_SCORE_DIFF = 0.01
CONFIDENT_SCORE = 0.25  # the default detection threshold of Ultralytics
MAX_LEFTOVER_SHARE = 0.01

OUTPUT_ONE_TO_ONE = "detections_xyxy_conf_cls"
OUTPUT_RAW = "raw_xywh_scores_needs_nms"


def letterbox(image_bgr: np.ndarray, size: int) -> np.ndarray:
    """The preprocessing a consumer must apply: fit, pad with 114, RGB, 0..1, NCHW float32."""
    import cv2

    height, width = image_bgr.shape[:2]
    scale = min(size / height, size / width)
    new_width, new_height = round(width * scale), round(height * scale)
    resized = cv2.resize(image_bgr, (new_width, new_height), interpolation=cv2.INTER_LINEAR)
    left = (size - new_width) // 2
    top = (size - new_height) // 2
    canvas = np.full((size, size, 3), 114, dtype=np.uint8)
    canvas[top : top + new_height, left : left + new_width] = resized
    rgb = canvas[:, :, ::-1].astype(np.float32) / 255.0
    return rgb.transpose(2, 0, 1)[None]


def decode(output: np.ndarray, conf: float, iou: float, max_det: int, size: int) -> tuple[np.ndarray, str]:
    """Rows of x1 y1 x2 y2 score class, in input pixels, for a batch of one.

    Embedded NMS returns boxes reaching outside the input. PyTorch clips them while mapping to the
    image, so a consumer has to clip as well: model.meta.json says so, and this is what it means.
    """
    if output.ndim == 3 and output.shape[-1] == 6:
        rows = output[0]
        rows = rows[rows[:, 4] >= conf].copy()
        kind = OUTPUT_ONE_TO_ONE
    else:
        import torch
        from ultralytics.utils.nms import non_max_suppression

        rows = non_max_suppression(torch.from_numpy(output), conf, iou, max_det=max_det)[0].numpy().copy()
        kind = OUTPUT_RAW
    rows[:, :4] = np.clip(rows[:, :4], 0, size)
    return rows, kind


def box_iou(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    top_left = np.maximum(a[:, None, :2], b[None, :, :2])
    bottom_right = np.minimum(a[:, None, 2:4], b[None, :, 2:4])
    intersection = np.prod(np.clip(bottom_right - top_left, 0, None), axis=2)
    area_a = np.prod(a[:, 2:4] - a[:, :2], axis=1)
    area_b = np.prod(b[:, 2:4] - b[:, :2], axis=1)
    return intersection / np.maximum(area_a[:, None] + area_b[None, :] - intersection, 1e-9)


def compare(reference: np.ndarray, candidate: np.ndarray) -> dict[str, Any]:
    """Pairs exact matches, so one odd box cannot steal another's partner. What stays unpaired on
    either side goes to ``leftover_scores``, for judge() to weigh.
    """
    ious: list[float] = []
    score_diffs: list[float] = []
    if not len(reference) or not len(candidate):
        scores = [float(score) for score in (*reference[:, 4], *candidate[:, 4])]
        return {"boxes": len(reference), "leftover_scores": scores, "ious": ious, "score_diffs": score_diffs}

    same_class = reference[:, 5][:, None] == candidate[:, 5][None, :]
    overlap = box_iou(reference, candidate) * same_class
    free_rows = set(range(len(reference)))
    free_columns = set(range(len(candidate)))
    for row in np.argsort(-reference[:, 4]):
        row = int(row)
        for column in np.argsort(-overlap[row]):
            column = int(column)
            if overlap[row, column] < MIN_BOX_IOU:
                break
            if column in free_columns:
                ious.append(float(overlap[row, column]))
                score_diffs.append(abs(float(reference[row, 4] - candidate[column, 4])))
                free_rows.discard(row)
                free_columns.discard(column)
                break

    leftovers = [float(reference[row, 4]) for row in free_rows] + [float(candidate[column, 4]) for column in free_columns]
    return {"boxes": len(reference), "leftover_scores": leftovers, "ious": ious, "score_diffs": score_diffs}


def judge(totals: dict[str, Any]) -> tuple[str, list[str]]:
    """Box by box, at the threshold metrics use. ``inconclusive`` when nothing was detected:
    agreeing on zero boxes proves nothing.

    A box a user would act on must match exactly. Far below that, two boxes overlapping near the NMS
    threshold can swap on a rounding difference (seen on YOLO11: one pair of 6000, score 0.045), so
    the low-score tail may differ within a small share, and the report always counts it.

    mAP does not decide this. Ultralytics evaluates an ONNX file without the clipping a consumer must
    apply, so its gap measures that path, not the model. The report still shows it.
    """
    problems: list[str] = []
    leftovers = totals["leftover_scores"]
    confident = [score for score in leftovers if score >= CONFIDENT_SCORE]
    if confident:
        problems.append(
            f"{len(confident)} box điểm từ {CONFIDENT_SCORE} trở lên không có cặp khớp giữa PyTorch và ONNX (IoU cần từ {MIN_BOX_IOU})"
        )
    if len(leftovers) > max(totals["boxes"], 1) * MAX_LEFTOVER_SHARE:
        problems.append(f"{len(leftovers)} box không có cặp khớp (IoU cần từ {MIN_BOX_IOU}), quá {MAX_LEFTOVER_SHARE:.0%} số box")
    if totals["score_diffs"] and max(totals["score_diffs"]) > MAX_SCORE_DIFF:
        problems.append(f"Chênh điểm lớn nhất {max(totals['score_diffs']):.4f}, cần dưới {MAX_SCORE_DIFF}")
    if problems:
        return "failed", problems
    if not totals["boxes"]:
        return "inconclusive", ["Model không phát hiện box nào trên ảnh kiểm tra nên chưa so sánh được"]
    return "passed", []


def _val_images(paths: RunPaths) -> list[Path]:
    import yaml

    doc = yaml.safe_load(paths.resolved_data.read_text(encoding="utf-8"))
    root = dataset_root(paths.resolved_data, doc)
    found: list[Path] = []
    for directory in _split_dirs(root, doc["val"]):
        found += sorted(p for p in directory.rglob("*") if p.suffix.lower() in IMAGE_SUFFIXES)
    return found[:MAX_IMAGES]


def run(config: dict[str, Any], paths: RunPaths, events: EventLog) -> int:
    import cv2
    import onnxruntime
    import torch
    from ultralytics import YOLO

    _prepare()
    if not paths.best.exists():
        events.error("no_checkpoint", "Run này chưa có best.pt để export.")
        return 2

    size = int(config["imgsz"])
    embed_nms = config["onnx"]["nms"]
    events.emit("export", format="onnx", stage="exporting", nms=embed_nms)
    exported = Path(
        YOLO(str(paths.best)).export(
            format="onnx", imgsz=size, nms=embed_nms, conf=0.001, iou=IOU, max_det=MAX_DET, device="cpu", simplify=True
        )
    )
    paths.exports.mkdir(exist_ok=True)
    target = paths.exports / "model.onnx"
    os.replace(exported, target)

    events.emit("export", format="onnx", stage="verifying")
    import onnx

    graph = onnx.load(str(target))
    onnx.checker.check_model(graph)
    opset = max(item.version for item in graph.opset_import if item.domain in ("", "ai.onnx"))
    del graph

    providers = [p for p in ("CUDAExecutionProvider", "CPUExecutionProvider") if p in onnxruntime.get_available_providers()]
    session = onnxruntime.InferenceSession(str(target), providers=providers)
    input_name = session.get_inputs()[0].name

    # False selects the one-to-one head of models that have one. Without it PyTorch answers from the
    # one-to-many head, which is a different set of outputs: the reference must be the exported branch.
    head_selector = False if embed_nms is False else None
    reference_model = YOLO(str(paths.best))
    totals: dict[str, Any] = {"boxes": 0, "leftover_scores": [], "ious": [], "score_diffs": []}
    latencies: list[float] = []
    output_kind = None
    images = _val_images(paths)
    for image_path in images:
        image = cv2.imdecode(np.fromfile(str(image_path), dtype=np.uint8), cv2.IMREAD_COLOR)
        if image is None:
            continue
        tensor = letterbox(image, size)
        started = time.perf_counter()
        output = session.run(None, {input_name: tensor})[0]
        latencies.append((time.perf_counter() - started) * 1000)
        candidate, output_kind = decode(output, COMPARE_CONF, IOU, MAX_DET, size)
        result = reference_model.predict(
            source=torch.from_numpy(tensor.copy()),
            conf=COMPARE_CONF,
            iou=IOU,
            max_det=MAX_DET,
            imgsz=size,
            device="cpu",
            verbose=False,
            nms=head_selector,
        )[0]
        reference = result.boxes.data.cpu().numpy()
        pair = compare(reference, candidate)
        totals["boxes"] += pair["boxes"]
        totals["leftover_scores"] += pair["leftover_scores"]
        totals["ious"] += pair["ious"]
        totals["score_diffs"] += pair["score_diffs"]

    common = {
        "data": str(paths.resolved_data),
        "imgsz": size,
        "batch": 1,
        "conf": 0.001,
        "iou": IOU,
        "max_det": MAX_DET,
        "device": "cpu",
        "rect": False,
        "plots": False,
        "verbose": False,
        "workers": 0,
        "project": str(paths.root),
        "name": "val-export",
        "exist_ok": True,
        "nms": head_selector,
    }
    map_pytorch = finite(YOLO(str(paths.best)).val(**common).box.map)
    map_onnx = finite(YOLO(str(target), task="detect").val(**common).box.map)
    shutil.rmtree(paths.root / "val-export", ignore_errors=True)
    map_diff = abs(map_pytorch - map_onnx) if map_pytorch is not None and map_onnx is not None else None

    status, problems = judge(totals)
    names = list(YOLO(str(paths.best)).names.values())
    dataset_report = json.loads(paths.dataset_report.read_text(encoding="utf-8"))
    meta = {
        "schema": 1,
        "model": config["model"],
        "model_family": config["model_family"],
        "task": "detect",
        "license": config["license"],
        "classes": names,
        "input": {
            "name": input_name,
            "shape": [1, 3, size, size],
            "type": "float32",
            "layout": "NCHW",
            "color": "RGB",
            "range": [0.0, 1.0],
            "resize": "letterbox, center, pad 114",
        },
        "output": {
            "kind": output_kind,
            "columns": ["x1", "y1", "x2", "y2", "score", "class"] if output_kind == OUTPUT_ONE_TO_ONE else None,
            "coordinates": "pixels of the letterboxed input",
            "nms_embedded": bool(embed_nms),
            "embedded_conf": 0.001 if embed_nms else None,
            "embedded_iou": IOU if embed_nms else None,
            "postprocess": [
                "A fixed number of rows, padded with zeros: keep the rows whose score reaches your threshold.",
                "Clip x1, x2, y1, y2 to the input size: boxes may reach outside it.",
                "Undo the letterbox to get coordinates on the original image.",
            ],
        },
        "opset": opset,
        "ultralytics": _version("ultralytics"),
        "image_digest": config.get("image_digest"),
        "dataset_fingerprint": dataset_report["fingerprint"],
        "synthetic_dataset": dataset_report["synthetic"],
        "verification": status,
    }
    report = {
        "status": status,
        "problems": problems,
        "nms_setting": embed_nms,
        "images": len(latencies),
        "compare_conf": COMPARE_CONF,
        "boxes_compared": totals["boxes"],
        "boxes_without_pair": len(totals["leftover_scores"]),
        "highest_score_without_pair": round(max(totals["leftover_scores"]), 5) if totals["leftover_scores"] else None,
        "min_box_iou": round(min(totals["ious"]), 5) if totals["ious"] else None,
        "max_score_diff": round(max(totals["score_diffs"]), 5) if totals["score_diffs"] else None,
        "map50_95_pytorch": map_pytorch,
        "map50_95_onnx": map_onnx,
        "map50_95_diff": map_diff,
        "latency_ms_median": round(statistics.median(latencies), 1) if latencies else None,
        "latency_device": session.get_providers()[0],
        "file_mb": round(target.stat().st_size / 1e6, 1),
        "output_shape": list(session.get_outputs()[0].shape),
    }
    write_json(paths.exports / "model.meta.json", meta)
    write_json(paths.exports / "export-report.json", report)
    events.emit("export_result", format="onnx", status=status, problems=problems, file="exports/model.onnx")
    return 0 if status != "failed" else 3


def _version(package: str) -> str:
    from importlib.metadata import version

    return version(package)
