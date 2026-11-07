# Bàn giao

Ngày: 2026-11-07

## Đã làm

- Module 3 Dataset: chia train/val/test (stratify theo class, theo thư mục gốc, tái lập theo seed), preprocessing (Stretch, Fit, Fill, ảnh xám, gộp hoặc bỏ class, bỏ ảnh không nhãn theo tỉ lệ), tăng cường chỉ cho train (lật, xoay 90°, xoay nhỏ, scale, dịch, shear, sáng, phơi sáng, tương phản, gamma, hue, bão hòa, ảnh xám, blur, motion blur, nhiễu, JPEG vỡ, cutout), preview 3×3 có vẽ nhãn, tạo và xóa version. Xem `docs/design/screens/DATASET.md`.
- Quyết định 0007: version chỉ đóng băng danh sách ảnh, split và config; ảnh tăng cường sinh lúc xuất, từ master, config và seed. Nhãn đọc lúc xuất.
- Mọi phép hình học gộp thành một ma trận affine (`RenderPlan`); ảnh và nhãn cùng đọc nó. Số ngẫu nhiên dùng `DeterministicRandom`, không dùng `System.Random`.
- Sửa nhỏ ngoài phạm vi: `Option<T>.ToString` trả về `Label` (UI Automation đọc tên mục combo qua đó).

## Đã kiểm tra

- `dotnet build`: 0 cảnh báo, 0 lỗi. `dotnet test`: 400/400 (Domain 134, Imaging 72, Application và Storage 194). Trước đó là 248.
- Đột biến kiểm chứng: tắt bước sửa class hiếm của `SplitService` thì 3 test mới thất bại, bật lại thì qua. Lần đầu các test chia tập không bắt được lỗi này (đột biến vẫn qua); đã bổ sung 3 bộ dữ liệu tìm được bằng dò ngẫu nhiên.
- Engine OpenCV: tâm khối trắng trong ảnh xuất trùng tâm nhãn đã biến đổi dưới 1 px cho 12 biến thể hình học, 3 chế độ resize, và ảnh 3200 px bị thu nhỏ mạnh. Lỗi thật tìm được nhờ test này: nhiễu từng ném exception (`SetArray` với Mat 3 kênh); đã sửa.
- Chạy app thật qua UI Automation trên 48 ảnh tổng hợp (6 thư mục × 8) đã qua Prepare và gán nhãn: trạng thái chưa project và chưa ảnh, số liệu, bảng class, cảnh báo, báo lỗi tỉ lệ 120%, nút Tạo version bị khóa khi config sai, ba mẫu tăng cường, preprocessing Fit và gộp class, tạo version, từ chối tên trùng, xóa có hộp xác nhận. Xem ảnh chụp: nhãn bám đúng vật thể sau xoay, dịch, scale, letterbox, gộp class.
- Kill tiến trình rồi đọc nguội DB: `integrity_check` ok, số ảnh mỗi tập khớp màn hình, không thư mục nào nằm ở hai tập, mọi class có mặt ở cả train và val, config JSON đủ.

## Chưa kiểm tra

- Ảnh thật và bộ vài trăm ảnh thật. Các mẫu tăng cường và ngưỡng cảnh báo (10 nhãn train tối thiểu, lệch 10 điểm phần trăm) là ước lượng.
- Chưa có exporter, nên chưa có gì đọc version để sinh dataset; engine chạy đúng qua test và preview, nhưng chưa chạy ở quy mô xuất hàng nghìn ảnh (nhiễu tạo mảng `short` cỡ số điểm ảnh: ảnh 20 MP × 3 kênh khoảng 120 MB một lần; nên đo khi viết exporter).
- Nhập số bằng bàn phím, thứ tự Tab, và hành vi khi đổi project trong lúc đang tính (có xử lý hủy trong code, chưa thử bằng tay).
- `DatasetViewModel` và các view không có test tự động riêng.
- Từ Prepare, Annotate: các mục đã ghi ở bàn giao trước (ảnh thật, một số đường UI chưa thử, installer trên máy sạch, ký số). Chưa chạy lại installer sau thay đổi này.

## Lưu ý cho Module 4

- OBB bị biên ảnh cắt được fit lại và có thể nhô vài pixel ra ngoài ảnh; exporter phải quyết định (xem DATASET.md).
- Mặc định chia theo thư mục: ít thư mục thì tỉ lệ thực tế lệch xa mục tiêu (6 thư mục ra khoảng 52/34/14). Màn hình đã cảnh báo, nhưng nên cân nhắc mặc định khi có dữ liệu thật.
- Tập tin kiểm thử trong scratchpad (`seed`, `smoke_dataset.ps1`) không nằm trong repo.

## Tiếp theo

Xem `tasks/active.md`. Chưa push, repo không có remote.
