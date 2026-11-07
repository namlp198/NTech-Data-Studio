# 0012: ONNX dùng NMS nhúng cho mọi phiên bản, kèm hợp đồng trong model.meta.json

Ngày: 2026-11-08

Bản thiết kế trước đề xuất YOLO26 dùng `nms=False`, các bản khác dùng `nms=True`. Chạy thật trên `ultralytics` 8.4.204 cho thấy khác.

**Đo được trên YOLO26n, 3 epoch, dataset tổng hợp:**

| Cách xuất | mAP50-95 PyTorch / ONNX | Kết quả |
| --- | --- | --- |
| `nms=True` | 0,1416 / 0,1449 | Khớp từng box |
| `nms=False` | 0,0442 / 0,0442 | Nhánh khác với metric lúc train (0,142) |

Metric lúc train đo trên nhánh một-nhiều kèm NMS (`val` đặt `end2end` theo `nms is False`). `nms=False` xuất nhánh một-một, là bộ đầu ra khác. Số này từ model mới train rất ít, nên chưa nói được khoảng cách giữa hai nhánh trên model đã train đủ.

**Quyết định:**

1. Mọi phiên bản xuất bằng `nms=True`, nên có cùng dạng `(1, 300, 6)`: file xuất ra đúng là thứ metric đã đo, bên dùng không phải viết NMS.
2. Bên dùng phải cắt box về khung đầu vào. NMS nhúng trả cả box vượt ra ngoài; PyTorch thì cắt. Hợp đồng này nằm trong `model.meta.json`.
3. Kiểm chứng làm theo đúng hợp đồng, không qua predictor của Ultralytics, và so từng box ở ngưỡng 0,001. Có box điểm từ 0,25 không có cặp khớp, hoặc quá 1% số box không có cặp, hoặc điểm lệch từ 0,01 thì thất bại. Không phát hiện box nào thì `inconclusive`, không phải đạt.
4. mAP qua ONNX chỉ báo để tham khảo.

**Hệ quả:**

- Không có lợi thế không cần NMS của YOLO26. Nếu cần, phải đánh giá riêng nhánh một-một, vì metric lúc train không đo nhánh đó.
- Câu hỏi còn mở: ở `nms=False`, mAP hai bên trùng khít nhưng so từng box qua `predict` thì lệch. Chưa rõ nguyên nhân.
- NMS nhúng có ảnh hưởng tới TensorRT hay không: chưa thử.
