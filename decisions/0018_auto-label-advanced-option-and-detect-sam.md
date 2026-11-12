# 0018: Gán nhãn tự động là tùy chọn nâng cao, thêm cách model detect + SAM

Ngày: 2026-11-11

Quyết định 0017 cho gán nhãn tự động bằng model segment của Trainer. Hai điều nảy ra khi dùng thật:

- Mask của YOLO-seg có độ phân giải thấp (một phần tư ảnh vào), nên polygon của vật thể nhỏ hoặc mảnh (băng dính trên dây) thô và không bám. Detect thì tìm vật thể tốt hơn với cùng lượng dữ liệu, còn SAM (đã có cho Magic polygon, quyết định 0016) vẽ viền bám theo pixel.
- Không phải ai cũng muốn thấy các nút gán tự động; chủ dự án muốn chúng là tùy chọn nâng cao, bật mới dùng được.

**Quyết định:**

1. **Công tắc nâng cao** "Bật gán nhãn tự động" ở Cài đặt (`AutoLabelEnabled`, mặc định **tắt**, lưu trong `settings.json`). Tắt thì Annotate không có nút "Gán tự động", "Gán tự động tất cả" (nút Dừng chỉ hiện khi đang chạy) và `ConfiguredAutoLabeler` từ chối chạy. Đường dẫn model đã chọn được giữ khi tắt. Người đã chọn model theo 0017 phải bật công tắc một lần.
2. **Hai cách gán**, chọn ở Cài đặt (`AutoLabelMode`): `Segment` (như 0017) và `DetectSam`. Mỗi cách có đường dẫn model riêng (`AutoLabelModel` cho segment, `AutoLabelDetectModel` cho detect) để đổi qua lại không mất cái nào. Ngưỡng tin cậy dùng chung.
3. **Detect + SAM**: `OnnxBoxDetector` (Imaging) đọc ONNX detect của Trainer theo hợp đồng `model.meta.json` (0012 của Trainer: `task: detect`, `output.kind: detections_xyxy_conf_cls`, NMS nhúng; cắt box về khung ảnh vào, hoàn tác letterbox, cắt về ảnh). `DetectThenSegmentLabeler` (Application) đưa mỗi box cho `ISmartSegmenter` dưới dạng box prompt. Ranh giới là `IBoxDetector`; Application không biết ONNX.
4. **Vùng phân tích SAM** do `SamRegionPlanner` quyết: box có cạnh dài dưới 24 px trong góc nhìn của SAM (ảnh được thu về 1024 ở cạnh dài) là "nhỏ xíu" và được phân tích theo một vùng quanh nó (các box nhỏ gần nhau dùng chung một vùng); mọi box khác dùng chung một lần phân tích cả ảnh. Lý do chọn ngưỡng thấp ở mục Hệ quả.
5. **Không để mất vật thể**: SAM không cắt được, hoặc đường cắt có hộp bao chồng với box (IoU) dưới 0,3, thì giữ box thành polygon 4 đỉnh (`AutoLabel.IsBoxOutline`); lần gán báo "n vật thể SAM không cắt được nên giữ nguyên khung" để người dùng sửa tay.
6. Phần còn lại giữ như 0017: nhãn `source = model`, một lần Undo, ảnh ở "Cần review", chấm tím, khớp class theo tên, bước đầu duyệt từng ảnh. Chọn cách detect + SAM đòi cả model detect lẫn model SAM (Cài đặt báo thiếu cái nào).

**Hệ quả:**

- Đo trên project wire harness của chủ dự án (SAM thật, hộp bao của polygon vẽ tay làm "box của detector hoàn hảo", so chồng lấp với polygon tay; test `DetectThenSegmentRealSamTests`, chạy khi đặt `NTECH_SAM_HANDLABELS`): ảnh 1024x576 (44 vật thể), phân tích theo vùng cho chồng lấp trung bình 0,77 còn cả ảnh là 0,83, mà chậm gấp khoảng 5 lần; ảnh 2048x1294 (43 vật thể) là 0,82 và 0,91, chậm gấp khoảng 4 lần. Nhìn bằng mắt ở độ phóng 4 lần, hai cách đều bám vật thể. Vì vậy vùng chỉ dành cho box nhỏ xíu (ngưỡng 24 px). Với các box nhỏ xíu (5 vật thể mỗi ảnh), vùng tốt hơn ở ảnh 1024 (0,74 so với 0,58) nhưng kém hơn ở ảnh 2048 (0,78 so với 0,87): chưa đủ để kết luận.
- Phép đo chưa phải chân lý: polygon tay của chủ dự án nhiều khả năng cũng vẽ bằng Magic polygon (SAM trên cả ảnh), nên cách "cả ảnh" có lợi thế sẵn khi so. **Chưa chạy với model detect thật**: chưa có file ONNX detect trên máy này; `OnnxBoxDetector` mới được test bằng tensor tổng hợp (`YoloBoxDecoderTests`) và bằng meta giả (`OnnxBoxDetectorTests`).
- Ngưỡng 24 px, hệ số bối cảnh quanh box (1 lần cạnh dài, tối thiểu 16 px) và ngưỡng IoU 0,3 là số đo thử, nên chỉnh khi có kết quả trên model detect thật.
- Mỗi vùng là một lần chạy encoder của SAM (khoảng 1 giây trên CPU): một ảnh có nhiều vật thể nhỏ xíu rải rộng chạy lâu hơn.
- Số thứ tự 0018 theo Data Studio. Nhánh Trainer đang có `0014_trainer-segment-task`, trùng số 0014 của Data Studio, và có thể đã đánh tiếp các số 0015 đến 0017; chưa xử lý.
