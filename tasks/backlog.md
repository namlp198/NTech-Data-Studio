# Backlog

## Module

- [x] Module 1 Prepare: import, audit, làm sạch, chuẩn hóa, hình học
- [x] Module 2 Annotate: canvas, công cụ vẽ, undo/redo, class, tự động lưu
- [x] Module 3 Dataset: split, preprocessing, augmentation, preview
- [x] Module 4 Export: YOLO26 (detect, segment, OBB, classify), COCO, validator
- [ ] Nhập nhãn có sẵn (YOLO txt, COCO json) vào project: có bộ đọc, chưa có service ghép và màn hình

## Module 1: còn lại

- [ ] Chạy nghiệm thu 1.000 ảnh BMP 20MP trên ảnh thật
- [ ] Áp dụng ICC profile nhúng trong ảnh; hiện mọi ảnh được coi là sRGB
- [ ] Audit tính cả xoay EXIF khi hiển thị độ phân giải
- [ ] Tinh chỉnh hệ số ước tính dung lượng ở Audit bằng số liệu ảnh thật

## Module 2: còn lại

- [ ] Tay cầm xoay cho rotated box, và ô kéo ở giữa cạnh box
- [ ] Chọn nhiều hình, Ctrl+C nhiều hình
- [ ] Đổi thứ tự và phím tắt của class (hiện tự gán, class thứ 10 trở đi không có phím)
- [ ] Giữ lịch sử undo khi rời màn hình Annotate
- [ ] Project test cho Desktop: `AnnotateViewModel.HandleKey`, lọc ảnh, đồng bộ chọn nhãn (hiện chỉ kiểm tra bằng chạy app)
- [ ] Thử zoom/pan trên ảnh thật, GPU yếu (render tier 0 hoặc 1), ảnh có cạnh trên 16.384 px
- [ ] Nếu ghi đồng bộ gây giật khi vẽ liên tục trên ổ chậm: hàng đợi ghi có thứ tự (xem quyết định 0006)
- [ ] Ảnh nằm một phần dưới các panel nổi khi vừa khung; cân nhắc vừa khung theo vùng không bị che

## Module 3: còn lại

- [ ] Tile và crop ROI tĩnh ở preprocessing của version (hiện chỉ có Stretch, Fit, Fill, ảnh xám, gộp class)
- [ ] Mặc định tăng cường theo profile (YOLO26, RF-DETR, Anomaly có mặc định riêng theo thiết kế); hiện chỉ có ba nút mẫu, chưa nối với model profile
- [ ] Chạy trên ảnh thật và bộ vài trăm ảnh thật; chỉnh các mẫu tăng cường (giá trị hiện là ước lượng)
- [ ] Project test cho Desktop: `DatasetViewModel` (debounce, hủy khi sửa tiếp, đồng bộ bảng class gộp) hiện chỉ kiểm tra bằng chạy app
- [ ] Snapshot `versions/<tên>/` đọc được bằng mắt, nếu cần; phải sinh từ DB (quyết định 0007)
- [ ] Giữ lại hoặc khôi phục thiết lập của lần tạo version gần nhất khi mở lại màn hình (hiện luôn về mặc định)
- [ ] Phím tắt và thứ tự Tab của form (chưa rà soát bằng bàn phím)

## Module 4: còn lại

- [ ] Train thử thật bằng bản xuất: `yolo train` (detect, segment, OBB, classify) và RF-DETR (COCO). Chưa chạy; các giả định chưa kiểm chứng: `data.yaml` không có `path:`, category COCO bắt đầu từ 1 không có category giữ chỗ, tên model mẫu trong lệnh train (`yolo26s-seg.pt`, `-obb`, `-cls`) và lệnh RF-DETR trong README
- [ ] Nhập nhãn có sẵn: ghép nhãn đọc được (`YoloLabelParser`, `CocoLabelParser`) vào ảnh của project qua `AnnotateService.Execute`; cần quyết định cách khớp ảnh (tên file, hash) và ánh xạ class (theo tên hoặc theo chỉ số)
- [ ] Project test cho Desktop: `ExportViewModel` (kiểm tra lại khi đổi lựa chọn, hủy kiểm tra cũ, khóa nút khi có lỗi) hiện chỉ kiểm tra bằng chạy app
- [ ] Đo trên quy mô thật: xuất vài nghìn ảnh 20 MP (RAM, thời gian; nhiễu tạo mảng cỡ số điểm ảnh) và thử ổ chậm; số luồng hiện tính theo ảnh lớn nhất của version
- [ ] Chọn riêng từng tập khi xuất (ví dụ chỉ val), và xuất lại chỉ phần nhãn không đụng tới ảnh; hiện luôn xuất trọn version
- [ ] Phím tắt và thứ tự Tab của form Export (chưa rà soát bằng bàn phím)

## Cần làm rõ


- [ ] Lộ trình 6 bước và 2 cổng: sơ đồ nhúng không có trong bản xuất Markdown của tài liệu thiết kế
- [ ] Đối chiếu `imgsz` và `stride` của profile RF-DETR và PatchCore với phiên bản sẽ dùng; giá trị hiện tại là giả định
- [ ] Đối chiếu `imgsz` của các profile YOLO26 ngoài detect; thiết kế chỉ cho mẫu của detect
- [ ] Thiết kế nêu profile Mask R-CNN ở Module 1 nhưng không có trong danh sách file `profiles/`

## Đóng gói

- [ ] Icon ứng dụng `Assets/AppIcon.ico`
- [x] `scripts/` build, package, deploy và `installer/NTech-DataStudio.iss`
- [ ] Ký số installer (cần chứng chỉ)
- [ ] Thử cài lên máy sạch chưa có .NET để xác nhận bản self-contained; mới thử trên máy dev
- [ ] `samples/` và `tests/TestData/`
- [ ] `docs/formats/`, `docs/product/SCREEN_REQUIREMENTS.md`, các file còn lại trong `docs/design/screens/`: viết khi làm module tương ứng
