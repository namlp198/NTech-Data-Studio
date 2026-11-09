# 0014: Trainer train được YOLO segment, ONNX segment có hai output và kiểm chứng riêng

Ngày: 2026-11-09

Quyết định 0009 giới hạn Trainer ở YOLO detect, và thiết kế để Segment ở mốc M6. Chủ dự án cần train segment, và Data Studio đã gán nhãn polygon và xuất YOLO segment. Quyết định này đưa Segment lên trước, không đổi kiến trúc: vẫn một engine, một image, một adapter.

**Quyết định:**

1. `task` là một trường của danh mục (`detect` hoặc `segment`), ghi vào `config.yaml` và `run.json` của mỗi run. Run cũ không có trường này đọc như `detect`.
2. Segment là một file danh mục riêng, `trainer/catalog/ultralytics-segment.yaml`, với họ `yolo26seg`, `yolo11seg`, `yolov8seg` (weights `yolo26{size}-seg.pt` và tương tự). Tên model là `yolo26segn`, `yolo11segs`... YOLO12 không có weights segment chính thức nên không có. Màn hình Cấu hình có ô chọn tác vụ; trường `display` giữ tên hiển thị `YOLO26`.
3. Cả ba họ ghi `experimental`, chưa phải `supported`: đã qua train, export và kiểm chứng ONNX trên GPU, nhưng chỉ với dataset tổng hợp (hình tròn và vuông), chưa có dataset polygon thật. Cũng chưa đo VRAM, nên đánh giá là Unknown và bước chạy thử sẽ đo.
4. Kiểm tra dataset theo task. Model segment gặp dòng nhãn box là **lỗi** (Ultralytics sẽ bỏ box hoặc train sai), cũng lỗi khi không có polygon nào. Model detect gặp polygon vẫn chỉ cảnh báo. Màn hình Dataset chưa có model nên chỉ mô tả (`stats.label_rows`) và cảnh báo khi dataset trộn hai loại.
5. Metric của segment tính trên **mask** (`metrics/...(M)`): các số trong sự kiện `epoch_end` và `result.json` là của mask, kèm mục `box` riêng. Giao diện ghi "MASK MAP50" cho run segment.
6. ONNX segment xuất bằng `nms=True` như quyết định 0012, nhưng có **hai output**: `output0` dạng `(1, 300, 6 + K)` (box, điểm, class, K hệ số mask, K = 32) và `output1` là proto `(1, K, H/4, W/4)`. Hợp đồng cho bên dùng nằm trong `model.meta.json`: mask là hệ số nhân proto, phóng bilinear lên cỡ đầu vào, cắt theo box của chính nó (x1 <= x < x2, y1 <= y < y2), giữ pixel có logit trên 0.

**Kiểm chứng ONNX segment** (so với PyTorch, trên tối đa 20 ảnh val, ngưỡng 0,001):

- Box: cùng luật như detect (quyết định 0012). Thêm: IoU mask giữa mask PyTorch vẽ và mask giải mã theo hợp đồng phải từ 0,95 trở lên với mọi box điểm từ 0,25.
- **Đối chiếu bằng mô hình thô, không bằng `predict`,** với model segment không có đầu end-to-end (YOLO11, YOLOv8): đầu ra thô của PyTorch cộng NMS theo class cùng ngưỡng với lúc xuất, và mask từ `ops.process_mask` của Ultralytics (độc lập với bộ giải mã tự viết). Lý do đo được: trên YOLO11-seg, đầu ra thô cộng NMS khớp 300/300 hàng với file ONNX, còn `predict` trả 207 hàng, thiếu nhiều box điểm cao (tới 0,46) dù IoU với box giữ lại dưới 0,7. File ONNX đúng; tập box của `predict` khác. Nguyên nhân bên trong `predict` chưa tìm ra. Khi so theo cách thô, box được ghép **chưa cắt** về khung, vì hai box thô khác nhau có thể cùng bị cắt thành toàn khung và ghép nhầm.
- mAP mask của ONNX chỉ để tham khảo, như detect.

**Đo được** (dataset tổng hợp 120 ảnh train, 320 px, 40 epoch, GPU RTX 4060 Ti, image CUDA):

| Họ | Box so sánh | Box không cặp | Chênh điểm lớn nhất | IoU mask thấp nhất | mAP mask PyTorch / ONNX |
| --- | --- | --- | --- | --- | --- |
| yolo26segn | 2730 | 5 | 0 | 0,998 | 0,6653 / 0,6654 |
| yolo11segn | 6000 | 0 | 0 | 0,989 | 0,4266 / 0,4262 |
| yolov8segn | 2837 | 0 | 0 | 0,9996 | 0,6348 / 0,6349 |

Số này từ model train rất ít trên dữ liệu tổng hợp: nói được là luồng đúng, chưa nói gì về chất lượng.

**Hệ quả:**

- Chưa làm: OBB, Classify; mặt nạ điểm ảnh PNG, RLE; hiển thị mask trong app (chỉ có con số). Bước TensorRT với ONNX segment chưa thử.
- Chưa có dataset polygon thật: cần chạy trên nhãn thật rồi mới nâng lên `supported`.
- Chưa giải thích được vì sao `predict` của Ultralytics cho tập box khác NMS nhúng với segment không-end2end. Nếu bản `ultralytics` nâng lên đổi hành vi này, đối chiếu thô vẫn đúng, nhưng phải chạy lại cả danh mục (quyết định 0009).
- Đường dẫn `D:/...` trong `path:` của data.yaml không được hiểu là tuyệt đối trong container Linux (xem test `test_absolute_path_from_another_machine...`): vấn đề có từ trước, không thuộc quyết định này.
