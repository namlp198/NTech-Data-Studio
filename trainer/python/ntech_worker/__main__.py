"""Entry point. Exit codes: 0 done, 2 refused before starting, 3 failed while working."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from . import dataset
from .events import EventLog
from .runtime import ResourceSampler, RunPaths, load_config, probe, tee_output, write_json


def _validate(args: argparse.Namespace) -> int:
    report = dataset.validate(Path(args.data), args.imgsz, args.task).as_dict()
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["ok"] else 2


def _is_oom(problem: BaseException) -> bool:
    return "out of memory" in str(problem).lower()


def _train(args: argparse.Namespace) -> int:
    paths = RunPaths(Path(args.run_dir))
    config = load_config(paths)
    events = EventLog(paths.events, config["run_id"])
    tee_output(paths)
    events.state("Preflight", resume=args.resume)
    sampler = ResourceSampler(events, paths.root)
    try:
        data_yaml = Path(config["data"])
        report = dataset.validate(data_yaml, config["imgsz"], config.get("task", "detect"))
        if args.resume and paths.dataset_report.exists():
            before = json.loads(paths.dataset_report.read_text(encoding="utf-8"))
            if before.get("fingerprint") != report.fingerprint:
                events.error("dataset_changed", "Dataset đã đổi so với lúc bắt đầu run này, không train tiếp được.")
                events.state("Failed")
                return 2
        write_json(paths.dataset_report, report.as_dict())
        for warning in report.warnings:
            events.warning(warning["code"], warning["message"])
        if report.errors:
            for error in report.errors:
                events.error(error["code"], error["message"])
            events.state("Failed")
            return 2
        dataset.write_resolved(data_yaml, paths.resolved_data)

        import torch

        if config["device"] != "cpu" and not torch.cuda.is_available():
            events.error("no_gpu_in_container", "Cấu hình chọn GPU nhưng container không thấy GPU.")
            events.state("Failed")
            return 2

        from . import ultralytics_adapter as adapter

        events.emit("capabilities", **adapter.CAPABILITIES)
        if not args.resume and config.get("preflight", True):
            events.emit("preflight", **adapter.preflight(config, paths, events))
        events.state("Running", resume=args.resume)
        sampler.start()
        final_state = adapter.train(config, paths, events, args.resume)
        sampler.stop()
        events.state("Evaluating")
        write_json(paths.result, adapter.evaluate(config, paths))
        events.state(final_state)
        return 0
    except Exception as problem:  # the host only learns about a crash through the event log
        sampler.stop()
        code = "out_of_memory" if _is_oom(problem) else "worker_error"
        message = "Hết bộ nhớ. Giảm batch hoặc imgsz rồi chạy lại." if _is_oom(problem) else f"{type(problem).__name__}: {problem}"
        events.error(code, message)
        events.state("Failed")
        raise


def _export(args: argparse.Namespace) -> int:
    paths = RunPaths(Path(args.run_dir))
    config = load_config(paths)
    events = EventLog(paths.events, config["run_id"])
    tee_output(paths)
    from . import onnx_export

    try:
        return onnx_export.run(config, paths, events)
    except Exception as problem:
        events.error("export_error", f"{type(problem).__name__}: {problem}")
        events.emit("export_result", format="onnx", status="failed", problems=[str(problem)], file=None)
        raise


def main() -> int:
    parser = argparse.ArgumentParser(prog="ntech_worker")
    commands = parser.add_subparsers(dest="command", required=True)

    commands.add_parser("probe")

    validate = commands.add_parser("validate")
    validate.add_argument("--data", required=True)
    validate.add_argument("--imgsz", type=int, default=640)
    validate.add_argument("--task", choices=dataset.TASKS, default=None)

    train = commands.add_parser("train")
    train.add_argument("--run-dir", required=True)
    train.add_argument("--resume", action="store_true")

    export = commands.add_parser("export")
    export.add_argument("--run-dir", required=True)

    args = parser.parse_args()
    if args.command == "probe":
        print(json.dumps(probe(), ensure_ascii=False))
        return 0
    if args.command == "validate":
        return _validate(args)
    if args.command == "train":
        return _train(args)
    return _export(args)


if __name__ == "__main__":
    sys.exit(main())
