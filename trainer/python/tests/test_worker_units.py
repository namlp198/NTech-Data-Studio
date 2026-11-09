"""Events, warning rules and ONNX comparison: everything testable without a framework or a GPU."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest

from ntech_worker import monitor, onnx_export
from ntech_worker.events import EventLog, finite
from ntech_worker.runtime import RunPaths, stop_requested


def read(path: Path) -> list[dict]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


def test_events_are_one_json_object_per_line_with_the_contract_fields(tmp_path: Path) -> None:
    log = EventLog(tmp_path / "events.jsonl", "run-1")
    log.state("Running")
    log.warning("x", "Hết bộ nhớ")
    first, second = read(tmp_path / "events.jsonl")
    assert first["schema"] == 1 and first["run"] == "run-1" and first["type"] == "state" and first["data"]["state"] == "Running"
    assert first["ts"].endswith("Z")
    assert second["id"] == first["id"] + 1
    assert second["data"]["message"] == "Hết bộ nhớ"


def test_ids_continue_after_a_resume_even_past_a_torn_line(tmp_path: Path) -> None:
    path = tmp_path / "events.jsonl"
    EventLog(path, "r").state("Running")
    with path.open("a", encoding="utf-8") as stream:
        stream.write('{"schema":1,"id":')
    log = EventLog(path, "r")
    assert log._next_id == 2


def test_nan_is_not_written_as_invalid_json() -> None:
    assert finite(float("nan")) is None
    assert finite(float("inf")) is None
    assert finite("abc") is None
    assert finite(1.5) == 1.5


def test_stop_request_tolerates_absent_and_half_written_files(tmp_path: Path) -> None:
    paths = RunPaths(tmp_path)
    assert not stop_requested(paths)
    paths.control.write_text('{"stop":', encoding="utf-8")
    assert not stop_requested(paths)
    paths.control.write_text('{"stop": true}', encoding="utf-8")
    assert stop_requested(paths)
    paths.control.write_text('{"stop": false}', encoding="utf-8")
    assert not stop_requested(paths)


def test_nan_loss_warns_once() -> None:
    rules = monitor.EpochRules()
    assert [w.code for w in rules.observe(None, 1.0, 0.1)] == ["loss_nan"]
    assert rules.observe(None, 1.0, 0.1) == []


def test_overfit_needs_val_rising_while_train_falls() -> None:
    rules = monitor.EpochRules()
    fired: list[str] = []
    for step in range(monitor.OVERFIT_EPOCHS + 1):
        fired += [w.code for w in rules.observe(5.0 - step * 0.1, 3.0 + step * 0.1, 0.5 + step * 0.01)]
    assert fired == ["overfit"]


def test_both_losses_falling_is_not_overfit() -> None:
    rules = monitor.EpochRules()
    for step in range(10):
        assert rules.observe(5.0 - step * 0.1, 3.0 - step * 0.1, 0.1 + step * 0.01) == []


def test_plateau_counts_from_the_best_map() -> None:
    rules = monitor.EpochRules()
    rules.observe(1.0, 1.0, 0.5)
    fired = []
    for _ in range(monitor.PLATEAU_EPOCHS):
        fired += [w.code for w in rules.observe(1.0, 1.0, 0.4)]
    assert fired == ["plateau"]


def test_hot_gpu_must_last_before_warning() -> None:
    rules = monitor.ResourceRules()
    for _ in range(monitor.GPU_HOT_SAMPLES - 1):
        assert rules.observe(90, 90, 10, 100) == []
    assert [w.code for w in rules.observe(90, 90, 10, 100)] == ["gpu_hot"]


def test_starved_gpu_and_low_disk() -> None:
    rules = monitor.ResourceRules()
    fired: list[str] = []
    for _ in range(monitor.GPU_IDLE_SAMPLES):
        fired += [w.code for w in rules.observe(5, 50, 95, 1.0)]
    assert set(fired) == {"gpu_starved", "disk_low"}


def test_cpu_only_run_raises_no_gpu_warnings() -> None:
    rules = monitor.ResourceRules()
    for _ in range(50):
        assert rules.observe(None, None, 99, 100) == []


def box(x1: float, y1: float, x2: float, y2: float, score: float = 0.9, cls: float = 0) -> list[float]:
    return [x1, y1, x2, y2, score, cls]


def test_identical_detections_pass() -> None:
    rows = np.array([box(0, 0, 10, 10), box(20, 20, 40, 40, 0.8, 1)], dtype=np.float32)
    pair = onnx_export.compare(rows, rows.copy())
    assert pair["leftover_scores"] == [] and min(pair["ious"]) == 1.0 and max(pair["score_diffs"]) == 0.0
    assert onnx_export.judge(pair)[0] == "passed"


def test_a_shifted_box_fails_on_iou() -> None:
    reference = np.array([box(0, 0, 100, 100)], dtype=np.float32)
    status, problems = onnx_export.judge(onnx_export.compare(reference, reference + [5, 5, 5, 5, 0, 0]))
    assert status == "failed" and "IoU" in problems[0]


def _many_exact_boxes(count: int, score: float = 0.9) -> np.ndarray:
    return np.array([box(i * 200, 0, i * 200 + 50, 50, score - i * 0.0001) for i in range(count)], dtype=np.float32)


def test_a_low_score_swap_among_many_is_reported_not_failed() -> None:
    """Seen for real on YOLO11: one pair of 6000 boxes at score 0.045 swapped near the NMS threshold."""
    exact = _many_exact_boxes(200)
    reference = np.vstack([exact, box(0, 0, 50, 36, 0.045)])
    candidate = np.vstack([exact, box(0, 0, 50, 30, 0.045)])
    pair = onnx_export.compare(reference, candidate)
    assert len(pair["leftover_scores"]) == 2
    assert onnx_export.judge(pair)[0] == "passed"


def test_one_confident_box_without_a_pair_fails_however_many_others_match() -> None:
    exact = _many_exact_boxes(1000)
    pair = onnx_export.compare(np.vstack([exact, box(900, 900, 950, 950, 0.25)]), exact)
    status, problems = onnx_export.judge(pair)
    assert status == "failed" and "0.25" in problems[0]


def test_a_low_score_tail_that_differs_too_much_fails() -> None:
    exact = _many_exact_boxes(100)
    tail = _many_exact_boxes(100, score=0.05) + [0, 1000, 0, 1000, 0, 0]
    pair = onnx_export.compare(np.vstack([exact, tail]), exact)
    assert onnx_export.judge(pair)[0] == "failed"


def test_one_odd_box_does_not_disturb_the_exact_pairs() -> None:
    exact = _many_exact_boxes(200)
    pair = onnx_export.compare(np.vstack([exact, box(0, 0, 50, 36, 0.95)]), exact)
    assert len(pair["ious"]) == 200 and min(pair["ious"]) == 1.0


def test_moved_boxes_fail() -> None:
    exact = _many_exact_boxes(200)
    assert onnx_export.judge(onnx_export.compare(exact, exact + [2, 0, 2, 0, 0, 0]))[0] == "failed"


def test_a_different_score_fails() -> None:
    reference = np.array([box(0, 0, 100, 100, 0.9)], dtype=np.float32)
    candidate = np.array([box(0, 0, 100, 100, 0.5)], dtype=np.float32)
    assert onnx_export.judge(onnx_export.compare(reference, candidate))[0] == "failed"


def test_a_different_class_or_missing_box_has_no_pair() -> None:
    reference = np.array([box(0, 0, 100, 100, 0.9, 0)], dtype=np.float32)
    other_class = np.array([box(0, 0, 100, 100, 0.9, 1)], dtype=np.float32)
    assert len(onnx_export.compare(reference, other_class)["leftover_scores"]) == 2
    assert onnx_export.compare(reference, np.empty((0, 6), dtype=np.float32))["leftover_scores"] == [pytest.approx(0.9)]


def test_agreeing_on_nothing_proves_nothing() -> None:
    empty = np.empty((0, 6), dtype=np.float32)
    status, _ = onnx_export.judge(onnx_export.compare(empty, empty))
    assert status == "inconclusive"


def test_decode_filters_padded_rows_of_a_one_to_one_output() -> None:
    output = np.zeros((1, 300, 6), dtype=np.float32)
    output[0, 0] = box(1, 2, 3, 4, 0.9, 2)
    rows, kind = onnx_export.decode(output, 0.05, 0.7, 300, 320)
    assert kind == onnx_export.OUTPUT_ONE_TO_ONE
    assert rows.shape == (1, 6) and rows[0, 5] == 2


def test_decode_clips_boxes_reaching_outside_the_input() -> None:
    """Without this, YOLO11, YOLOv8 and YOLO12 disagreed with PyTorch on edge boxes: IoU down to 0.30."""
    output = np.zeros((1, 300, 6), dtype=np.float32)
    output[0, 0] = box(-8, -2, 333, 400, 0.9, 0)
    rows, _ = onnx_export.decode(output, 0.05, 0.7, 300, 320)
    assert rows[0, :4].tolist() == [0, 0, 320, 320]


def test_letterbox_contract() -> None:
    image = np.zeros((100, 200, 3), dtype=np.uint8)
    image[:, :] = (0, 0, 255)  # red in BGR
    tensor = onnx_export.letterbox(image, 320)
    assert tensor.shape == (1, 3, 320, 320) and tensor.dtype == np.float32
    assert tensor[0, 0, 160, 160] == 1.0  # R channel first
    assert abs(float(tensor[0, 0, 0, 0]) - 114 / 255) < 1e-6  # padding


def _segment_output(rows: list[tuple[float, float, float, float, float, int]], coefficient: float, kept: int = 2) -> np.ndarray:
    out = np.zeros((1, 5, 6 + kept), dtype=np.float32)
    for index, row in enumerate(rows):
        out[0, index, :6] = row
        out[0, index, 6] = coefficient
    return out


def test_segment_decode_keeps_only_the_confident_rows_and_one_mask_each() -> None:
    protos = np.ones((1, 2, 4, 4), dtype=np.float32)  # all positive: the mask is limited by its box alone
    output = _segment_output([(8, 8, 24, 24, 0.9, 1), (0, 0, 8, 8, 0.01, 0)], coefficient=1.0)
    rows, masks, kind = onnx_export.decode_segment(output, protos, conf=0.25, size=32)
    assert kind == onnx_export.OUTPUT_SEGMENT
    assert rows.shape == (1, 6) and masks.shape == (1, 32, 32)
    assert masks[0].sum() == 16 * 16  # x and y from 8 up to, not including, 24
    assert masks[0][8, 8] and not masks[0][24, 24] and not masks[0][7, 8]


def test_segment_decode_clips_a_box_that_reaches_outside_the_input() -> None:
    protos = np.ones((1, 2, 4, 4), dtype=np.float32)
    output = _segment_output([(-5, -5, 40, 16, 0.9, 0)], 1.0)
    rows, masks, _ = onnx_export.decode_segment(output, protos, 0.25, 32)
    assert rows[0, :4].tolist() == [0, 0, 32, 16]
    assert masks[0].sum() == 32 * 16
    raw, raw_masks, _ = onnx_export.decode_segment(output, protos, 0.25, 32, clip=False)
    assert raw[0, :4].tolist() == [-5, -5, 40, 16]  # kept as the model gave them, to tell apart boxes that clip alike
    assert (raw_masks == masks).all()


def test_segment_decode_keeps_the_pixels_where_the_logit_is_positive() -> None:
    protos = np.full((1, 2, 4, 4), -1.0, dtype=np.float32)
    protos[0, 0, :, :2] = 1.0  # the left half of the prototype is positive
    _, masks, _ = onnx_export.decode_segment(_segment_output([(0, 0, 32, 32, 0.9, 0)], 1.0), protos, 0.25, 32)
    assert masks[0][:, :14].all() and not masks[0][:, 18:].any()


def test_mask_iou() -> None:
    a = np.zeros((8, 8), dtype=bool)
    a[:4] = True
    b = np.zeros((8, 8), dtype=bool)
    b[2:6] = True
    assert onnx_export.mask_iou(a, a) == 1.0
    assert onnx_export.mask_iou(a, b) == pytest.approx(1 / 3)
    assert onnx_export.mask_iou(np.zeros((8, 8), bool), np.zeros((8, 8), bool)) == 1.0


def test_compare_names_the_matched_pairs() -> None:
    reference = np.array([[0, 0, 10, 10, 0.9, 1], [20, 20, 30, 30, 0.8, 0]], dtype=np.float32)
    candidate = reference[::-1].copy()
    assert sorted(onnx_export.compare(reference, candidate)["pairs"]) == [(0, 1), (1, 0)]


def test_a_confident_mask_that_differs_fails_but_a_low_score_one_only_counts() -> None:
    base = {"boxes": 2, "leftover_scores": [], "ious": [1.0], "score_diffs": [0.0], "mask_ious": [0.2, 0.99]}
    status, problems = onnx_export.judge(base | {"confident_mask_ious": [0.2, 0.99]})
    assert status == "failed" and any("Mask" in problem for problem in problems)
    assert onnx_export.judge(base | {"confident_mask_ious": [0.97, 0.99]})[0] == "passed"
