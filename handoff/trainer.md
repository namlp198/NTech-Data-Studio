# Bàn giao: NTech Trainer

Bàn giao của Data Studio ở `handoff/latest.md`. Quy tắc làm song song ở `docs/README_AI_WORKFLOW.md`.

## Trạng thái (2026-11-07)

Thiết kế v0.3 ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md`, kèm 3 sơ đồ trong `docs/trainer/images/`. Chỉ có tài liệu, chưa có code. Đây là bản nháp thay cho Initial Design v0.1 của chủ dự án.

## Chủ dự án đã chốt

- Máy hiện tại chỉ để phát triển; máy train là Linux hoặc Windows cấu hình cao.
- TensorRT là tùy chọn theo profile máy.
- OCR chưa phải trọng tâm.
- Luồng cần chạy được ngay: dataset wire harness có sẵn → YOLO detect → ONNX.
- Bộ phiên bản bước đầu: YOLO26, YOLO11, YOLOv8, cùng YOLO12 thử nghiệm.
- Trainer và Data Studio ở chung repo, phát triển song song theo nhánh và worktree riêng.

## Thiết kế chính

- Cùng gói `ultralytics` có YOLOv5u, v8, v9, v10, 11, 12, 26 (đọc wheel 8.4.204, không chạy). Vì vậy dùng một image, một adapter và một danh mục `trainer/catalog/`, tách khỏi `profiles/` để không đổi màn hình Export.
- Phiên bản chỉ ghi `supported` sau khi qua kiểm thử train và export.
- Theo code đã đọc, trong ba bản hỗ trợ đầu tiên chỉ YOLO26 có đầu ONNX một-một, nên tham số export khai báo theo từng phiên bản. Đề xuất `nms=True` cho YOLOv8, YOLO11 chưa chạy thử.

## Đã kiểm tra

- Render 3 sơ đồ và xem bằng mắt.
- Đọc file trong wheel `ultralytics` 8.4.204.
- Thông tin thư viện còn lại lấy từ tài liệu trên web; phần lớn license là nguồn thứ cấp hoặc tuyên bố của nhà cung cấp.

## Chưa kiểm tra

- Chưa chạy train, export hay Docker có GPU.
- Ngưỡng VRAM và ngưỡng kiểm chứng ONNX trong thiết kế là giả định.

## Lưu ý

Ultralytics nêu rõ model train ra thuộc AGPL-3.0 kể cả dùng nội bộ nếu không có Enterprise License, và điều này áp dụng cho mọi phiên bản trong gói. Chủ dự án chưa trả lời câu này. Cần kết luận trước khi đưa model vào sản xuất hoặc bán.

## Tiếp theo

Mốc M0, xem `tasks/active.md`. Câu hỏi còn mở ở mục 14 của thiết kế; thiếu đường dẫn dataset thì không bắt đầu được M0.
