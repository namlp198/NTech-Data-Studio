# 0017: Gán nhãn tự động bằng model segment do Trainer xuất

Ngày: 2026-11-09

Gán polygon bằng tay cho ảnh nhiều vật thể quá chậm. Magic polygon (0016) vẫn cần bạn chỉ từng vật thể. Chủ dự án muốn gán tay 20 đến 30 ảnh, train một model segment bằng Trainer, rồi dùng model đó gán các ảnh còn lại, sửa chỗ sai bằng tay.

**Quyết định:**

1. Data Studio chạy file `.onnx` segment của Trainer qua ONNX Runtime (đã có cho SAM). Không gọi Trainer, không đọc dataset của Trainer, không nhúng code Ultralytics: chỉ đọc đầu ra theo hợp đồng ở `model.meta.json` (quyết định 0012 và 0014 của Trainer). Ranh giới là `IAutoLabeler` ở Application; `OnnxAutoLabeler` ở Imaging.
2. File model và `model.meta.json` phải ở cùng thư mục. Meta là nguồn duy nhất cho cỡ đầu vào, tên class, tên tensor. File có `task` không phải `segment`, hoặc không nhúng NMS, hoặc đầu ra khác `detections_xyxy_conf_cls_mask_coefficients`, bị từ chối kèm lý do, không đoán.
3. Giải mã đúng hợp đồng: letterbox (vừa khung, căn giữa, đệm 114), RGB 0..1; hàng `x1 y1 x2 y2 score class + K hệ số`; mask = hệ số nhân proto, phóng bilinear lên cỡ đầu vào, cắt theo box của chính nó (`x1 <= x < x2`), giữ logit trên 0; cắt box về khung; hoàn tác letterbox về pixel master. Mỗi vật thể lấy contour lớn nhất, làm gọn như SAM.
4. Class khớp **theo tên** (không phân biệt hoa thường). Class của model không có trong project thì bỏ vật thể đó và báo; không class nào khớp thì từ chối cả lần gán.
5. Nhãn tự động có `source = model` (cột đã có từ migration đầu), đi qua `AnnotateService.Execute` như một sửa nhãn duy nhất (một Undo gỡ cả lần gán). Ảnh thành `Labeled`, tức "Cần review"; chỉ nút Duyệt mới lên `Reviewed`. Không thêm trạng thái mới.
6. Ngưỡng tin cậy mặc định 0,4, chỉnh ở Cài đặt (5 đến 95%). Đường dẫn model cũng ở Cài đặt (settings.json), không nằm trong project.
7. Bước đầu duyệt **từng ảnh**. Duyệt hàng loạt để sau, khi model đủ mạnh.

**Hệ quả:**

- Chưa chạy được với model thật trong phiên này: test dùng tensor tổng hợp theo đúng hợp đồng (`YoloSegDecoderTests`). Cần một `.onnx` do Trainer xuất để chạy thử đầu cuối.
- Chưa có: gán hàng loạt (chạy nền, tiến độ, hủy), phím duyệt-và-sang-ảnh-kế, nhìn nhãn tự động khác màu nhãn tay, bộ lọc "chỉ nhãn tay", cảnh báo val lẫn nhãn tự động. Nhãn tự động sau khi bạn sửa polygon vẫn mang `source = model`.
- Nguy cơ tự củng cố sai: nhãn tự động chưa duyệt không nên vào train. Có thể dùng ô "Chỉ đưa ảnh được chọn vào dataset" ở Annotate để chặn; chưa có chặn tự động.
- Số thứ tự 0017 theo Data Studio; Trainer đang dùng 0014 cho quyết định segment của nó (trùng số với 0014 ở đây). Chưa xử lý.
