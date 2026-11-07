"""What one worker process needs regardless of the task: paths, config, log tee, resource sampling."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Any, TextIO

import psutil
import yaml

from .events import EventLog, finite
from .monitor import ResourceRules


GB = 1024**3  # the figure Windows and nvidia-smi show


@dataclass
class RunPaths:
    root: Path

    @property
    def config(self) -> Path:
        return self.root / "config.yaml"

    @property
    def events(self) -> Path:
        return self.root / "events.jsonl"

    @property
    def control(self) -> Path:
        return self.root / "control.json"

    @property
    def log(self) -> Path:
        return self.root / "train.log"

    @property
    def dataset_report(self) -> Path:
        return self.root / "dataset.json"

    @property
    def resolved_data(self) -> Path:
        return self.root / "data.resolved.yaml"

    @property
    def result(self) -> Path:
        return self.root / "result.json"

    @property
    def engine_dir(self) -> Path:
        """Everything Ultralytics writes: weights, results.csv, args.yaml."""
        return self.root / "train"

    @property
    def best(self) -> Path:
        return self.engine_dir / "weights" / "best.pt"

    @property
    def last(self) -> Path:
        return self.engine_dir / "weights" / "last.pt"

    @property
    def resume_checkpoint(self) -> Path:
        """Ultralytics strips the optimizer from last.pt when a run ends, so a stop keeps its own copy."""
        return self.root / "checkpoints" / "resume.pt"

    @property
    def exports(self) -> Path:
        return self.root / "exports"


def load_config(paths: RunPaths) -> dict[str, Any]:
    return yaml.safe_load(paths.config.read_text(encoding="utf-8"))


def stop_requested(paths: RunPaths) -> bool:
    try:
        return bool(json.loads(paths.control.read_text(encoding="utf-8")).get("stop"))
    except (OSError, ValueError, AttributeError):
        return False  # absent, or the host is halfway through writing it: see it next epoch


def write_json(path: Path, value: Any) -> None:
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temporary, path)


class _Tee:
    def __init__(self, stream: TextIO, sink: TextIO) -> None:
        self._stream = stream
        self._sink = sink

    def write(self, text: str) -> int:
        self._stream.write(text)
        self._sink.write(text)
        return len(text)

    def flush(self) -> None:
        self._stream.flush()
        self._sink.flush()

    def isatty(self) -> bool:
        return False

    def __getattr__(self, name: str) -> Any:
        return getattr(self._stream, name)


def tee_output(paths: RunPaths) -> None:
    """Call before importing Ultralytics: its logger keeps the stream it finds at import time."""
    sink = paths.log.open("a", encoding="utf-8", buffering=1, errors="replace")
    sys.stdout = _Tee(sys.stdout, sink)  # type: ignore[assignment]
    sys.stderr = _Tee(sys.stderr, sink)  # type: ignore[assignment]


def gpu_sample() -> dict[str, float | None] | None:
    if shutil.which("nvidia-smi") is None:
        return None
    try:
        output = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=utilization.gpu,memory.used,memory.total,temperature.gpu",
                "--format=csv,noheader,nounits",
            ],
            capture_output=True,
            text=True,
            timeout=5,
            check=True,
        ).stdout
    except (OSError, subprocess.SubprocessError):
        return None
    first = output.strip().splitlines()[0] if output.strip() else ""
    parts = [part.strip() for part in first.split(",")]
    if len(parts) != 4:
        return None
    percent, used, total, temperature = (finite(part) for part in parts)
    return {"percent": percent, "vram_used_mb": used, "vram_total_mb": total, "temp_c": temperature}


class ResourceSampler(threading.Thread):
    def __init__(self, events: EventLog, disk_path: Path, interval_s: float = 10.0) -> None:
        super().__init__(daemon=True)
        self._events = events
        self._disk_path = disk_path
        self._interval = interval_s
        self._rules = ResourceRules()
        self._halt = threading.Event()

    def stop(self) -> None:
        self._halt.set()

    def run(self) -> None:
        psutil.cpu_percent()  # the first reading is meaningless
        while not self._halt.wait(self._interval):
            gpu = gpu_sample()
            cpu = psutil.cpu_percent()
            memory = psutil.virtual_memory()
            disk_free_gb = shutil.disk_usage(self._disk_path).free / GB
            self._events.emit(
                "resource",
                gpu=gpu,
                cpu_percent=cpu,
                ram_used_gb=round(memory.used / GB, 2),
                ram_total_gb=round(memory.total / GB, 2),
                disk_free_gb=round(disk_free_gb, 1),
            )
            for warning in self._rules.observe(
                gpu["percent"] if gpu else None, gpu["temp_c"] if gpu else None, cpu, disk_free_gb
            ):
                self._events.warning(warning.code, warning.message)


def cgroup_memory_limit_gb() -> float | None:
    """The memory this container may really use, which can be far below what the host has."""
    try:
        text = Path("/sys/fs/cgroup/memory.max").read_text().strip()
    except OSError:
        return None
    return None if text == "max" else round(int(text) / GB, 2)


def probe() -> dict[str, Any]:
    """Level 3 of the hardware check: what the runtime sees from inside the container."""
    import torch

    memory = psutil.virtual_memory()
    shm = shutil.disk_usage("/dev/shm") if os.path.isdir("/dev/shm") else None
    result: dict[str, Any] = {
        "torch": torch.__version__,
        "torch_cuda": torch.version.cuda,
        "cuda_available": torch.cuda.is_available(),
        "cpu_count": psutil.cpu_count(),
        "ram_total_gb": round(memory.total / GB, 2),
        "cgroup_limit_gb": cgroup_memory_limit_gb(),
        "shm_gb": round(shm.total / GB, 2) if shm else None,
        "gpu": None,
        "gpu_compute_ok": False,
    }
    if torch.cuda.is_available():
        result["gpu"] = {
            "name": torch.cuda.get_device_name(0),
            "vram_total_gb": round(torch.cuda.get_device_properties(0).total_memory / GB, 2),
        }
        try:  # the wheel may not support this architecture even though CUDA reports the device
            tensor = torch.ones(8, 8, device="cuda")
            result["gpu_compute_ok"] = float((tensor @ tensor).sum().item()) == 512.0
        except RuntimeError as problem:
            result["gpu_error"] = str(problem)
    return result
