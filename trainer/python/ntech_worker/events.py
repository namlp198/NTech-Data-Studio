"""Durable structured events.

The worker appends one JSON object per line to ``events.jsonl``. The host keeps a read offset, so it
can close and reopen at any time without losing anything. This file is the only contract between
the two; never parse the text log for state.
"""

from __future__ import annotations

import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SCHEMA = 1


class EventLog:
    def __init__(self, path: Path, run_id: str) -> None:
        self.path = Path(path)
        self.run_id = run_id
        self._next_id = self._read_next_id()

    def _read_next_id(self) -> int:
        """Continue numbering after a resume, so ids stay unique within a run."""
        if not self.path.exists():
            return 1
        last = 0
        with self.path.open(encoding="utf-8") as stream:
            for line in stream:
                try:
                    last = max(last, int(json.loads(line)["id"]))
                except (ValueError, KeyError, TypeError):
                    continue  # a torn final line from a killed worker
        return last + 1

    def emit(self, event_type: str, **data: Any) -> None:
        event = {
            "schema": SCHEMA,
            "id": self._next_id,
            "run": self.run_id,
            "ts": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "type": event_type,
            "data": data,
        }
        self._next_id += 1
        line = json.dumps(event, ensure_ascii=False, allow_nan=False, default=_jsonable)
        with self.path.open("a", encoding="utf-8", newline="\n") as stream:
            stream.write(line + "\n")
            stream.flush()
            os.fsync(stream.fileno())

    def state(self, name: str, **data: Any) -> None:
        self.emit("state", state=name, **data)

    def warning(self, code: str, message: str, **data: Any) -> None:
        self.emit("warning", code=code, message=message, **data)

    def error(self, code: str, message: str, **data: Any) -> None:
        self.emit("error", code=code, message=message, **data)


def _jsonable(value: Any) -> Any:
    """numpy scalars and paths reach here; everything else is a bug worth seeing as text."""
    if hasattr(value, "item"):
        return value.item()
    return str(value)


def finite(value: Any) -> float | None:
    """JSON has no NaN or infinity; report them as null and let the monitor raise the warning."""
    try:
        number = float(value)
    except (TypeError, ValueError):
        return None
    return number if number == number and abs(number) != float("inf") else None
