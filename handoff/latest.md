# Bàn giao

Ngày: 2026-11-07

## Đã làm

- Module 4 Export: kiểm tra trước khi xuất (`DatasetValidator`), xuất YOLO26 detect, segment, OBB, classify và COCO detect, segment (`ExportService` + `NTech.DataStudio.Exporters`), `README.dataset.md` và `version.json` trong mỗi bản xuất, tùy chọn zip, màn hình Export. Xem `docs/design/screens/EXPORT.md`.
- Quyết định 0008: các lựa chọn khi ghi file (không có `path:` trong `data.yaml`, OBB co về tâm cho vừa ảnh, nhãn không biểu diễn được thì bỏ và báo, id class liên tục từ 0 và COCO từ 1, ảnh không chỉnh thì chép nguyên master, tên file ASCII, ghi vào `.partial` rồi đổi tên).
- Ảnh và nhãn của mỗi bản xuất đọc cùng `RenderPlan` với preview ở Dataset (`RenderPlanner` + `AugmentSampler` + `OpenCvAugmentationEngine` + `LabelTransformer`). Hình nhãn do `LabelShapes` quyết định cho mọi định dạng.
- Bộ đọc nhãn `YoloLabelParser` và `CocoLabelParser` (dùng cho test vòng đọc lại). Chưa nối vào project.
- Exporters trước đó rỗng; giờ có code và project test.

## Đã kiểm tra

- `dotnet build`: 0 cảnh báo, 0 lỗi. `dotnet test`: 486/486 (Domain 134, Imaging 72, Exporters 23, Application và Storage 257). Trước đó là 400.
- `ExportService` chạy trên project thật (thư mục tiếng Việt, ảnh master JPEG thật, engine OpenCV): đúng cấu trúc thư mục; ảnh không chỉnh được chép nguyên byte; nhân ×3 chỉ ở train; nhãn bám khối trắng trong ảnh sau lật, xoay, scale, dịch (tâm lệch dưới 3 px, kiểm tra 20+ ảnh); cùng một version xuất hai lần ra cùng từng byte; hủy giữa chừng hoặc một master hỏng không để lại thư mục nào; không ghi đè thư mục lạ; zip; COCO; phân loại; class bị bỏ không để lỗ hổng id.
- Đột biến: cho `ExportService` tính nhãn bằng `RenderPlan` không tăng cường thì đúng một test (căn nhãn) thất bại; khôi phục thì qua.
- Chạy app thật qua UI Automation trên 48 ảnh tổng hợp: chưa project, chưa version, xuất YOLO detect, lần xuất thứ hai bị từ chối rồi qua khi bật ghi đè, Segment bị chặn (ảnh chỉ có box), phân loại bị chặn (chưa có tag), COCO kèm zip, imgsz "abc" bị báo lỗi. Đọc file trên đĩa: 69 ảnh train, 69 file nhãn, 138 dòng nhãn khớp README, mọi tọa độ trong 0..1. Vẽ nhãn từ file `.txt` lên ảnh đã xuất: khung bám đúng khối màu kể cả bản xoay và dịch.
- Lỗi thật tìm được khi chạy app: lần vào Export đầu tiên không kiểm tra gì và nút Xuất bị khóa (kiểm tra bị bỏ qua vì `IsLoading` còn true). Đã sửa; chạy lại thì có kết quả ngay.

## Chưa kiểm tra

- **Chưa chạy `yolo train` hay RF-DETR bằng bản xuất.** Các giả định chưa kiểm chứng: Ultralytics đọc `data.yaml` không có `path:` (lấy thư mục chứa file làm gốc) và từ chối ảnh có tọa độ OBB ngoài 0..1; RF-DETR đọc category COCO bắt đầu từ 1 không có category giữ chỗ; tên model và lệnh mẫu trong README (`yolo26s-seg.pt`, `-obb`, `-cls`, lệnh RF-DETR). Python và các thư viện này không được dùng trong repo (AGPL).
- Ảnh thật, bộ vài trăm ảnh thật. Chưa đo xuất vài nghìn ảnh 20 MP (RAM, thời gian). Số luồng tính theo ảnh lớn nhất của version.
- Nhãn polygon, rotated box và tag trên app thật: bản chạy app chỉ có box. Các đường này được kiểm bằng test (Segment, OBB, phân loại, kể cả OBB nhô ra ngoài), chưa bằng một bản xuất thật.
- `ExportViewModel` và view không có test tự động riêng. Phím tắt, thứ tự Tab của form. Nút Mở thư mục và hộp chọn thư mục (`Chọn thư mục...`) chưa bấm thử. Hủy khi đang xuất qua nút Hủy chưa bấm thử trong app (đã kiểm bằng test ở tầng service).
- Chưa chạy lại installer sau thay đổi này (không đổi script hay cách publish; Exporters vốn đã được Desktop tham chiếu).
- Nhập nhãn có sẵn vào project: chỉ có bộ đọc, chưa có service ghép ảnh và chưa có màn hình.

## Lưu ý

- Bộ đếm "nhãn dưới 50" trong cảnh báo tính trên nhãn gốc của version, còn bảng trong README tính trên bản xuất đã nhân ×N; hai số khác nhau là bình thường.
- Test nhiều tập cần mỗi ảnh một `source_image_id` riêng (ảnh cùng nguồn luôn cùng tập); `AnnotateFixture` mặc định cho toàn train. Xem CLAUDE.md.
- Tập tin kiểm thử trong scratchpad (`seed`, `smoke_export.ps1`) không nằm trong repo.

## Tiếp theo

Xem `tasks/active.md`. Chưa push, repo không có remote.

## Phiên sau Module 4: bốn yêu cầu bổ sung (2026-11-07)

- **Pan khi zoom:** khi ảnh đã zoom lớn hơn vừa khung, giữ chuột trái trên chỗ trống rồi kéo thì pan (công cụ Chọn và Tag). Bấm vào nhãn thì vẫn chọn và kéo nhãn; công cụ vẽ vẫn dùng Space hoặc chuột giữa để pan. Cuộn chuột vẫn zoom. `AnnotationEditor.IsOverLabel` quyết định có bấm trúng nhãn không (3 test).
- **Làm sạch:** chỉ "Trùng hoàn toàn (SHA-256)" bật sẵn; gần trùng, mờ, quá tối, cháy sáng tắt. Chỉ đổi mặc định ở màn hình; `CleanOptions` ở Application giữ mặc định cũ.
- **Phím tắt đổi công cụ:** Q (công cụ kế) và Shift+Q (công cụ trước), S như V (Chọn), + và - để zoom. `AnnotationEditor.CycleTool` (2 test).
- **Segmentation:** vốn đã có (công cụ Polygon, YOLO segment, COCO segment, profile `yolo26-segment` và `rf-detr-segment`). Đã thêm profile `maskrcnn-segment` (COCO segment), kèm ghi chú train riêng trong README bản xuất, và mục Segmentation ở `EXPORT.md`. Chưa có mặt nạ điểm ảnh (PNG), RLE hay segmentation ngữ nghĩa.
- Test: 492 (Domain 134, Imaging 72, Exporters 23, Application và Storage 263). Build 0 cảnh báo.
- **Chạy app thật:** mặc định làm sạch đọc lại đúng (chỉ SHA-256 bật); `maskrcnn-segment` có trong danh sách profile; kéo chuột trái khi đã zoom làm ảnh dịch đúng, công cụ Box không pan; Q và Shift+Q đi hết 5 công cụ hai chiều.
- **Lỗi tìm được khi chạy app:** máy kiểm có bộ gõ tiếng Việt Telex bật, nên gõ `e` lặp thành `ê` làm app nhận Backspace và bỏ mất phím. Vì vậy dùng Q thay E. Phím chữ đơn có sẵn (A, D, S, R...) vẫn có thể bị Telex làm hỏng khi bật tiếng Việt; tắt IME ở WPF không chặn được. Ghi ở `ANNOTATE.md` và `CLAUDE.md`. Cần cân nhắc phím tắt không phải chữ nếu người dùng thường gõ tiếng Việt khi gán nhãn.
- **Chưa kiểm tra:** pan khi kéo trên polygon lõm hoặc khi nhiều nhãn chồng nhau (chỉ kiểm tra vùng trống); pan trên màn hình cảm ứng; `maskrcnn-segment` chưa train thật (imgsz 800 và stride 32 là giả định).
