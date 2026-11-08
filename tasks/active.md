# Task đang làm

Ưu tiên hiện tại là NTech Trainer. Hai phần phát triển song song theo `docs/README_AI_WORKFLOW.md`.

## Trainer

Luồng dataset → YOLO detect (YOLO26, YOLO11, YOLOv8, YOLO12 thử nghiệm) → ONNX đã chạy thật bằng CLI và bằng app Desktop Avalonia (M2), trên Windows và Ubuntu trong WSLg, với image CPU và dataset tổng hợp. Thiết kế ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md` (v0.4), cách dùng ở `trainer/README.md`, tình trạng ở `handoff/trainer.md`.

Việc còn lại của M0 và M1 cần máy hoặc dữ liệu chưa có:
- Build image `cuda` và chạy trên máy train có GPU.
- Chạy trên dataset wire harness thật khi gán nhãn xong.
- Thử trên máy Ubuntu thật.

Việc làm được ngay là phần còn lại của M3: hàng đợi nhiều phiên bản.

## Data Studio

Đã xong, chờ chủ dự án thử trên ảnh thật: task mục tiêu của project (quyết định 0014), màn hình Cài đặt với phím tắt đổi được (quyết định 0015) và công cụ Magic polygon tự tách vật thể bằng SAM (quyết định 0016; cần đặt file model, xem handoff). Module 1 đến 4 vẫn còn cần chủ dự án thử trên ảnh thật. Nhập nhãn có sẵn vào project lùi lại vì luồng Trainer đọc thẳng dataset YOLO (xem `tasks/backlog.md`).
