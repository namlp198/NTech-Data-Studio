# Bàn giao

Ngày: 2026-11-06

## Đã làm

- Nền móng MVP với namespace `NTech.DataStudio.*` (quyết định 0002).
- Module 1 Prepare: wizard Import, Audit, Làm sạch, Chuẩn hóa (`docs/design/screens/PREPARE.md`).
- Module 2 Annotate: canvas zoom/pan, box, polygon, rotated box, tag, sửa hình, class, undo/redo, trạng thái ảnh, Null và duyệt, lọc, chép nhãn ảnh trước (`docs/design/screens/ANNOTATE.md`). Quyết định 0006: ghi DB trước, rồi mới đổi bộ nhớ.
- Build và phát hành: `scripts\build|package|deploy` (`.ps1` và `.bat`), `installer\NTech-DataStudio.iss`.

## Đã kiểm tra

- `dotnet build`: 0 cảnh báo, 0 lỗi. `dotnet test`: 248/248 (Domain 63, Imaging 39, Application và Storage 146).
- Chạy app thật bằng UI Automation và chuột thật: tạo project, đi hết Prepare, thêm class (kể cả báo trùng tên), vẽ box, polygon, rotated box, chọn, xóa bằng phím Delete, Ctrl+Z, Ctrl+Y, kéo di chuyển, phím D, N, T, 1, A, F. Sau đó kill tiến trình rồi đọc DB nguội: nhãn, tag, trạng thái đủ và `integrity_check` ok.
- Đo `AnnotationCanvas` với 200 nhãn: 20 MP ở 55 fps, 50 MP ở 59 fps (xem ANNOTATE.md). Chưa cần ảnh pyramid ở mức này.

## Chưa kiểm tra

- Ảnh thật. Zoom/pan chỉ đo trên ảnh tổng hợp, GPU tier 2.
- Trên UI thật: Gộp và Xóa class (logic có test ở tầng dưới), Ẩn và Khóa nhãn, bộ lọc, nút Chép nhãn ảnh trước, nút Duyệt, bấm nhãn trong danh sách để zoom tới, Space-pan và chuột giữa, cuộn chuột zoom bằng chuột thật, bấm đúp thêm đỉnh polygon.
- `AnnotateViewModel` và các view không có test tự động riêng.
- Installer trên máy sạch chưa có .NET, và cảnh báo SmartScreen vì chưa ký số.
- Từ Prepare: kéo thả thư mục, hộp thoại chọn thư mục, nút Hủy, hộp thoại đổi cấu hình, ảnh 16-bit trên UI.

## Tiếp theo

Xem `tasks/active.md`. Repo đã `git init` nhưng chưa có commit nào.