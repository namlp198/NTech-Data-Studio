"""Warning rules, separate from any training framework so they can be tested without a GPU.

The thresholds are initial assumptions (design section 9.3), to be calibrated on real runs.
"""

from __future__ import annotations

from dataclasses import dataclass, field

GPU_HOT_C = 85
GPU_HOT_SAMPLES = 6
GPU_IDLE_PERCENT = 30
CPU_BUSY_PERCENT = 70
GPU_IDLE_SAMPLES = 12
DISK_LOW_GB = 2.0
OVERFIT_EPOCHS = 5
PLATEAU_EPOCHS = 20


@dataclass
class Warning:
    code: str
    message: str


@dataclass
class EpochRules:
    """Fed once per epoch. Each warning fires once per run, so a long run does not bury the log."""

    _fired: set[str] = field(default_factory=set)
    _val_losses: list[float] = field(default_factory=list)
    _train_losses: list[float] = field(default_factory=list)
    _best_map: float = -1.0
    _since_best: int = 0

    def _once(self, code: str, message: str) -> list[Warning]:
        if code in self._fired:
            return []
        self._fired.add(code)
        return [Warning(code, message)]

    def observe(self, train_loss: float | None, val_loss: float | None, map50_95: float | None) -> list[Warning]:
        out: list[Warning] = []
        if train_loss is None or val_loss is None:
            out += self._once("loss_nan", "Loss không còn là số. Kiểm tra nhãn và learning rate.")
        else:
            self._train_losses.append(train_loss)
            self._val_losses.append(val_loss)
            out += self._check_overfit()
        if map50_95 is not None:
            if map50_95 > self._best_map:
                self._best_map = map50_95
                self._since_best = 0
            else:
                self._since_best += 1
                if self._since_best >= PLATEAU_EPOCHS:
                    out += self._once(
                        "plateau", f"mAP không cải thiện sau {PLATEAU_EPOCHS} epoch. Có thể dừng sớm."
                    )
        return out

    def _check_overfit(self) -> list[Warning]:
        window = OVERFIT_EPOCHS + 1
        if len(self._val_losses) < window:
            return []
        val = self._val_losses[-window:]
        train = self._train_losses[-window:]
        val_rising = all(b > a for a, b in zip(val, val[1:]))
        train_falling = all(b < a for a, b in zip(train, train[1:]))
        if val_rising and train_falling:
            return self._once(
                "overfit",
                f"Val loss tăng trong khi train loss giảm suốt {OVERFIT_EPOCHS} epoch. Có thể đang overfit.",
            )
        return []


@dataclass
class ResourceRules:
    """Fed once per resource sample."""

    _fired: set[str] = field(default_factory=set)
    _hot: int = 0
    _idle: int = 0

    def _once(self, code: str, message: str) -> list[Warning]:
        if code in self._fired:
            return []
        self._fired.add(code)
        return [Warning(code, message)]

    def observe(
        self,
        gpu_percent: float | None,
        gpu_temp_c: float | None,
        cpu_percent: float | None,
        disk_free_gb: float | None,
    ) -> list[Warning]:
        out: list[Warning] = []
        self._hot = self._hot + 1 if gpu_temp_c is not None and gpu_temp_c >= GPU_HOT_C else 0
        if self._hot >= GPU_HOT_SAMPLES:
            out += self._once("gpu_hot", f"GPU từ {GPU_HOT_C} °C trở lên kéo dài. Kiểm tra tản nhiệt.")
        starving = (
            gpu_percent is not None
            and cpu_percent is not None
            and gpu_percent < GPU_IDLE_PERCENT
            and cpu_percent > CPU_BUSY_PERCENT
        )
        self._idle = self._idle + 1 if starving else 0
        if self._idle >= GPU_IDLE_SAMPLES:
            out += self._once("gpu_starved", "GPU rảnh trong khi CPU bận: đang nghẽn ở khâu đọc dữ liệu.")
        if disk_free_gb is not None and disk_free_gb < DISK_LOW_GB:
            out += self._once("disk_low", f"Đĩa còn dưới {DISK_LOW_GB:g} GB. Dọn đĩa trước khi hết chỗ ghi checkpoint.")
        return out
