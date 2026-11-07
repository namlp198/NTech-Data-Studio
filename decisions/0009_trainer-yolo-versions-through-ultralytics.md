# 0009: Trainer bắt đầu bằng YOLO detect nhiều phiên bản qua gói ultralytics

Ngày: 2026-11-08

`DESIGN_DOC` ghi Trainer mặc định dùng RF-DETR, YOLO là tùy chọn có license. Chủ dự án cần chạy ngay luồng dataset wire harness → YOLO detect → ONNX, và cần train được nhiều phiên bản.

**Quyết định:**

1. Luồng đầu tiên là YOLO detect. Bộ đã chốt: YOLO26, YOLO11, YOLOv8 ở mức hỗ trợ, YOLO12 ở mức thử nghiệm.
2. Mọi phiên bản đi qua gói `ultralytics`, một image và một adapter, vì gói này train chúng qua cùng một API.
3. Danh mục nằm ở `trainer/catalog/`, tách khỏi `profiles/`. `profiles/` điều khiển màn hình Export của Data Studio; thêm file vào đó sẽ làm đổi màn hình.
4. Thêm phiên bản cùng engine chỉ cần thêm một mục danh mục, không sửa C#. Phiên bản chỉ được ghi `supported` sau khi đã chạy thật qua train, export và kiểm chứng ONNX.
5. Code Ultralytics chỉ nằm trong image và trong `ultralytics_adapter.py`, không nằm trong C# hay installer.

**Hệ quả:**

- Metadata gói `ultralytics` ghi AGPL-3.0, và Ultralytics nêu rõ model train ra cũng thuộc AGPL kể cả dùng nội bộ, trừ khi có Enterprise License. Bản YOLO thấp hơn không tránh được điều này (YOLOv8, YOLO11 cùng gói; YOLO12 repo riêng cũng AGPL). **Cần kết luận license trước khi đưa model vào sản xuất hoặc bán.** Chủ dự án chưa trả lời câu này.
- Engine khác (RF-DETR, repo YOLOv5 gốc, YOLOv7, YOLOX) là một adapter mới trong worker.
- Nâng `ultralytics` phải chạy lại toàn bộ danh mục.
