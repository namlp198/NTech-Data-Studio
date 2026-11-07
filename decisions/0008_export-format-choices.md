# 0008: Các lựa chọn khi ghi định dạng YOLO và COCO

Ngày: 2026-11-07

Thiết kế mô tả khung của Module 4 (cấu trúc thư mục, dòng nhãn, kiểm tra trước khi xuất) nhưng để ngỏ vài điểm mà exporter buộc phải chọn. Các chọn lựa dưới đây ảnh hưởng tới việc dataset có train được hay không, nên ghi lại ở đây.

**Quyết định:**

1. **`data.yaml` không có khóa `path:`.** Bản thiết kế minh họa có `path: D:/datasets/...`. Đường dẫn tuyệt đối làm bản xuất hỏng ngay khi chép sang máy khác hoặc đổi tên thư mục. Không có `path`, Ultralytics lấy thư mục chứa `data.yaml` làm gốc, nên `train: images/train` luôn đúng. Chưa kiểm chứng bằng một lần `yolo train` thật (xem Chưa kiểm tra trong `handoff/latest.md`).
2. **OBB bị biên ảnh đẩy ra ngoài được co lại về tâm cho vừa ảnh, không kẹp từng góc.** `LabelTransformer` fit lại OBB bằng hình chữ nhật diện tích nhỏ nhất nên góc có thể nhô vài pixel ra ngoài. Kẹp từng góc làm hình thôi là hình chữ nhật; Theo cách Ultralytics kiểm nhãn (nhớ từ `verify_image_label`, chưa chạy thử) một tọa độ ngoài 0..1 làm cả ảnh bị bỏ. Co đều về tâm giữ nguyên hình chữ nhật và hướng, chỉ mất một phần nhỏ diện tích. Tâm nằm ngoài ảnh thì nhãn bị bỏ. (`LabelShapes.FitInside`.)
3. **Nhãn không biểu diễn được thì bỏ, không ghi hỏng.** Point không có chỗ trong detect, segment, OBB, COCO. Polygon thành box bao quanh (detect), box thành polygon 4 đỉnh (segment), polygon thành hình chữ nhật nhỏ nhất (OBB). Validator báo trước từng loại chuyển đổi dưới dạng cảnh báo, và số nhãn bị bỏ hiện trong kết quả. Hai chuyển đổi mất thông tin (polygon sang box hoặc OBB) vì vậy là cảnh báo, không im lặng.
4. **Id class của dataset là số liên tục từ 0, theo thứ tự class, sau khi áp bảng gộp class.** Class bị gộp hoặc bỏ không để lại lỗ hổng. COCO dùng cùng thứ tự nhưng id bắt đầu từ 1.
5. **Ảnh không bị chỉnh gì thì chép nguyên file master**, không đọc rồi mã hóa lại: cùng điểm ảnh, không mất thêm chất lượng. Ảnh bị chỉnh được mã hóa JPEG chất lượng 95 (hoặc PNG nếu chọn).
6. **Tên file chỉ có ASCII** (`000012_anh_goc`, bản tăng cường thêm `_a1`, `_a2`), tên thư mục class cũng vậy; tên class trong `data.yaml` và json giữ nguyên chữ có dấu. Tránh công cụ nào đó vấp đường dẫn tiếng Việt.
7. **Ghi vào `<đích>.partial` rồi đổi tên.** Hủy hoặc lỗi giữa chừng thì xóa bản dở; không bao giờ có thư mục trông như đã xuất xong mà thiếu ảnh. Ghi đè chỉ áp cho thư mục có `version.json` do app ghi; thư mục khác bị từ chối kể cả khi bật ghi đè, vì thao tác này xóa thư mục.
8. **Lỗi chặn xuất, cảnh báo thì không.** Danh sách lỗi và cảnh báo nằm ở `DatasetValidator`. "Ảnh chỉ có box" ở profile Segment là lỗi (theo thiết kế); ảnh có cả box lẫn polygon thì box được đổi thành polygon 4 đỉnh kèm cảnh báo.

**Hệ quả:**

- Cùng một version xuất hai lần ra cùng từng byte (ảnh, nhãn, yaml). Test `TheSameVersionExportsTheSameBytesTwice` giữ điều này.
- Việc sinh ảnh dùng đúng `RenderPlanner` + `OpenCvAugmentationEngine` + `LabelTransformer` của Module 3, nên preview ở Dataset là bản xem trước thật của bản xuất.
- Định dạng COCO theo cách bố trí Roboflow (`train/`, `valid/`, `test/`) mà RF-DETR đọc; id category bắt đầu từ 1, không có category giữ chỗ id 0. Chưa kiểm chứng với một lần train RF-DETR thật; nếu một phiên bản `rfdetr` đòi category giữ chỗ thì chỉ sửa `CocoExporter`.
