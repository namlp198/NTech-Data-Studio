# Task đang làm

Module 4 Export đã xong phần code và chạy được; Module 1 đến 4 còn cần chủ dự án thử trên ảnh thật, và bản xuất cần được train thử thật (`yolo train`, RF-DETR). Task tiếp theo ứng viên: nhập nhãn có sẵn (YOLO txt, COCO json) vào project. Bộ đọc `YoloLabelParser` và `CocoLabelParser` đã có và đã test; còn thiếu service ghép nhãn vào ảnh của project (khớp tên file với ảnh master hoặc nguồn, đổi tọa độ chuẩn hóa sang pixel master, ánh xạ class) và màn hình nhập. Cần hỏi chủ dự án cách ghép ảnh trước khi làm (xem `tasks/backlog.md`).
