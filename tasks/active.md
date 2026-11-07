# Task đang làm

Ưu tiên hiện tại là NTech Trainer. Hai phần phát triển song song theo `docs/README_AI_WORKFLOW.md`.

## Trainer

Luồng: dataset có sẵn (kiểm tra wire harness) → chọn phiên bản YOLO, train detect → xuất ONNX. Phải train được nhiều phiên bản: YOLO26, YOLO11, YOLOv8 trước, YOLO12 thử nghiệm. Thiết kế ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md` (v0.3, bản nháp), chưa có code.

Việc tiếp theo là mốc M0: dựng image `cpu` và `cuda`, chạy thử luồng trên dataset thật với từng phiên bản, khóa phiên bản thư viện, chốt tham số export của từng bản. Trước khi làm cần chủ dự án cho biết đường dẫn và định dạng dataset, cùng thông tin máy train (mục 14 của thiết kế). Ghi các quyết định vào `decisions/` sau khi kiểm chứng (mục 15).

## Data Studio

Module 1 đến 4 vẫn còn cần chủ dự án thử trên ảnh thật. Nhập nhãn có sẵn vào project lùi lại vì luồng Trainer đọc thẳng dataset YOLO (xem `tasks/backlog.md`).
