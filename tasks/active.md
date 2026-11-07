# Task đang làm

Ưu tiên hiện tại là NTech Trainer. Hai phần phát triển song song theo `docs/README_AI_WORKFLOW.md`.

## Trainer

Luồng dataset → YOLO detect (YOLO26, YOLO11, YOLOv8, YOLO12 thử nghiệm) → ONNX đã chạy thật bằng CLI, trên image CPU và dataset tổng hợp. Thiết kế ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md` (v0.4), cách dùng ở `trainer/README.md`, tình trạng ở `handoff/trainer.md`.

Việc còn lại của M0 và M1 cần máy hoặc dữ liệu chưa có: build image `cuda` và chạy trên máy train có GPU; chạy trên dataset wire harness thật khi gán nhãn xong; train từ phía Linux. Việc làm được ngay là M2 (Desktop bằng Avalonia).

## Data Studio

Module 1 đến 4 vẫn còn cần chủ dự án thử trên ảnh thật. Nhập nhãn có sẵn vào project lùi lại vì luồng Trainer đọc thẳng dataset YOLO (xem `tasks/backlog.md`).
