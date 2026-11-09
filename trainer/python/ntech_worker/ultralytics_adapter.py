"""The only module that trains through Ultralytics (AGPL-3.0; see design section 10).

Every YOLO version in the catalog goes through here, since the package trains them all the same way.
Another engine, such as RF-DETR, would be another module with the same three operations.
"""

from __future__ import annotations

import gc
import os
import shutil
import time
from pathlib import Path
from typing import Any

from .events import EventLog, finite
from .monitor import EpochRules
from .runtime import RunPaths, stop_requested

CAPABILITIES = {"pause": False, "stop_at_epoch_end": True, "resume": True}
PREFLIGHT_BATCHES = 3
PROGRESS_INTERVAL_S = 5.0


class _PreflightDone(Exception):
    pass


def _prepare() -> None:
    """Weights land in the shared cache. No sync back to Ultralytics, and no experiment trackers."""
    from ultralytics import settings

    weights = Path("/cache/weights")
    weights.mkdir(parents=True, exist_ok=True)
    os.chdir(weights)
    outward = ("sync", "clearml", "comet", "dvc", "mlflow", "neptune", "raytune", "tensorboard", "wandb")
    # The list differs between package versions, and updating an unknown key raises.
    settings.update({key: False for key in outward if key in settings})


def _train_args(config: dict[str, Any], paths: RunPaths) -> dict[str, Any]:
    return {
        "data": str(paths.resolved_data),
        "imgsz": config["imgsz"],
        "epochs": config["epochs"],
        "batch": config["batch"],
        "device": config["device"],
        "workers": config["workers"],
        "seed": config["seed"],
        "patience": config["patience"],
        "plots": False,
        "project": str(paths.root),
        "exist_ok": True,
        **(config.get("overrides") or {}),
    }


def _vram_peak_gb() -> float | None:
    import torch

    if not torch.cuda.is_available():
        return None
    return round(torch.cuda.max_memory_reserved() / 1024**3, 2)


def preflight(config: dict[str, Any], paths: RunPaths, events: EventLog) -> dict[str, Any]:
    """Real batches through the real trainer: data loading, forward, backward, measured VRAM."""
    from ultralytics import YOLO

    _prepare()
    marks: list[float] = []
    batches_per_epoch: list[int] = []

    def on_batch_end(trainer: Any) -> None:
        marks.append(time.monotonic())
        batches_per_epoch[:] = [len(trainer.train_loader)]
        if len(marks) >= PREFLIGHT_BATCHES:
            raise _PreflightDone

    model = YOLO(config["weights"])
    model.add_callback("on_train_batch_end", on_batch_end)
    scratch = paths.root / "preflight"
    args = _train_args(config, paths) | {"name": "preflight", "epochs": 1, "val": False}
    try:
        model.train(**args)
    except _PreflightDone:
        pass
    seconds_per_batch = (marks[-1] - marks[0]) / max(len(marks) - 1, 1) if marks else None
    vram = _vram_peak_gb()
    trainer = getattr(model, "trainer", None)
    used_batch = getattr(trainer, "batch_size", None)
    del model, trainer
    gc.collect()
    _empty_cuda_cache()
    shutil.rmtree(scratch, ignore_errors=True)
    probe_file = paths.root / ".write-test"
    probe_file.write_bytes(b"0" * (1 << 20))
    probe_file.unlink()
    estimate = seconds_per_batch * batches_per_epoch[0] if seconds_per_batch and batches_per_epoch else None
    return {
        "seconds_per_batch": round(seconds_per_batch, 3) if seconds_per_batch else None,
        "batches_per_epoch": batches_per_epoch[0] if batches_per_epoch else None,
        "train_seconds_per_epoch_estimate": round(estimate, 1) if estimate else None,
        "vram_peak_gb": vram,
        "batch": used_batch,
    }


def _empty_cuda_cache() -> None:
    import torch

    if torch.cuda.is_available():
        torch.cuda.empty_cache()
        torch.cuda.reset_peak_memory_stats()


def primary_suffix(config: dict[str, Any]) -> str:
    """Ultralytics names metrics ``metrics/mAP50(B)`` for boxes and ``(M)`` for masks. A segment model
    is judged by its masks, a detect model by its boxes.
    """
    return "(M)" if config.get("task", "detect") == "segment" else "(B)"


def _sum_losses(metrics: dict[str, Any], prefix: str) -> float | None:
    values = [finite(value) for key, value in metrics.items() if key.startswith(prefix) and key.endswith("_loss")]
    if not values or any(value is None for value in values):
        return None
    return float(sum(values))  # type: ignore[arg-type]


def train(config: dict[str, Any], paths: RunPaths, events: EventLog, resume: bool) -> str:
    """Runs to the end or to a requested stop. Returns the final state name."""
    from ultralytics import YOLO

    _prepare()
    rules = EpochRules()
    seen_epochs: set[int] = set()
    stopped = {"value": False}
    last_progress = {"at": 0.0, "epoch": -1, "batch": 0}
    started = time.monotonic()
    epochs_done_here = {"count": 0}

    def on_batch_end(trainer: Any) -> None:
        if last_progress["epoch"] != trainer.epoch:
            last_progress["epoch"] = trainer.epoch
            last_progress["batch"] = 0
        last_progress["batch"] += 1
        now = time.monotonic()
        if now - last_progress["at"] < PROGRESS_INTERVAL_S:
            return
        last_progress["at"] = now
        events.emit(
            "progress",
            epoch=trainer.epoch + 1,
            epochs=trainer.epochs,
            batch=last_progress["batch"],
            batches=len(trainer.train_loader),
        )

    def on_fit_epoch_end(trainer: Any) -> None:
        metrics = dict(trainer.metrics or {})
        # The final evaluation of best.pt calls this hook too, with epoch + 1 and no val losses.
        if not any(key.startswith("val/") for key in metrics):
            return
        epoch = trainer.epoch + 1
        if epoch in seen_epochs:  # an epoch redone after out-of-memory or NaN recovery
            return
        seen_epochs.add(epoch)
        epochs_done_here["count"] += 1
        per_epoch = (time.monotonic() - started) / epochs_done_here["count"]
        losses = trainer.label_loss_items(trainer.tloss, prefix="train")
        train_loss = _sum_losses(losses, "train/")
        val_loss = _sum_losses(metrics, "val/")
        mark = primary_suffix(config)
        map50_95 = finite(metrics.get(f"metrics/mAP50-95{mark}"))
        events.emit(
            "epoch_end",
            epoch=epoch,
            epochs=trainer.epochs,
            train_loss=train_loss,
            val_loss=val_loss,
            precision=finite(metrics.get(f"metrics/precision{mark}")),
            recall=finite(metrics.get(f"metrics/recall{mark}")),
            map50=finite(metrics.get(f"metrics/mAP50{mark}")),
            map50_95=map50_95,
            vram_peak_gb=_vram_peak_gb(),
            batch=trainer.batch_size,
            eta_seconds=round(per_epoch * (trainer.epochs - epoch)),
        )
        for warning in rules.observe(train_loss, val_loss, map50_95):
            events.warning(warning.code, warning.message, epoch=epoch)
        if stop_requested(paths) and epoch < trainer.epochs and Path(trainer.last).exists():
            paths.resume_checkpoint.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(trainer.last, paths.resume_checkpoint)
            events.emit("checkpoint_saved", kind="resume", epoch=epoch)
            stopped["value"] = True
            trainer.stop = True

    def on_model_save(trainer: Any) -> None:
        events.emit("checkpoint_saved", kind="last", epoch=trainer.epoch + 1)

    if resume:
        checkpoint = paths.resume_checkpoint if paths.resume_checkpoint.exists() else paths.last
        model = YOLO(str(checkpoint))
        arguments: dict[str, Any] = {"resume": str(checkpoint)}
    else:
        model = YOLO(config["weights"])
        arguments = _train_args(config, paths) | {"name": paths.engine_dir.name}

    model.add_callback("on_train_batch_end", on_batch_end)
    model.add_callback("on_fit_epoch_end", on_fit_epoch_end)
    model.add_callback("on_model_save", on_model_save)
    model.train(**arguments)

    if stopped["value"]:
        return "Cancelled"
    if paths.resume_checkpoint.exists():
        paths.resume_checkpoint.unlink()  # finished: nothing left to continue from
    return "Completed"


def evaluate(config: dict[str, Any], paths: RunPaths) -> dict[str, Any]:
    """Metrics of best.pt on val, overall and per class. Missed defects hide inside an overall mAP."""
    from ultralytics import YOLO

    _prepare()
    metrics = YOLO(str(paths.best)).val(
        data=str(paths.resolved_data),
        imgsz=config["imgsz"],
        device=config["device"],
        workers=config["workers"],
        plots=False,
        project=str(paths.root),
        name="val",
        exist_ok=True,
        verbose=False,
    )
    task = config.get("task", "detect")
    # The headline numbers are the ones the task is judged by: masks for segment, boxes for detect.
    main = metrics.seg if task == "segment" else metrics.box
    names = metrics.names
    classes = []
    for index, class_id in enumerate(main.ap_class_index):
        class_id = int(class_id)
        classes.append(
            {
                "id": class_id,
                "name": names[class_id],
                "precision": finite(main.p[index]),
                "recall": finite(main.r[index]),
                "map50": finite(main.ap50[index]),
                "map50_95": finite(main.ap[index]),
            }
        )
    shutil.rmtree(paths.root / "val", ignore_errors=True)
    result = {
        "task": task,
        "precision": finite(main.mp),
        "recall": finite(main.mr),
        "map50": finite(main.map50),
        "map50_95": finite(main.map),
        "classes": classes,
    }
    if task == "segment":
        # A mask that is right needs a box that is right, so the box numbers stay visible.
        box = metrics.box
        result["box"] = {
            "precision": finite(box.mp),
            "recall": finite(box.mr),
            "map50": finite(box.map50),
            "map50_95": finite(box.map),
        }
    return result
