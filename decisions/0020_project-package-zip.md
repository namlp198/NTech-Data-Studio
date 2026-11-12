# 0020: Đóng gói project thành file .zip có version

Ngày: 2026-11-11

Project là một thư mục tự chứa (`docs/DESIGN_DOC.md`), nhưng chép cả thư mục sang máy khác hay gửi cho người khác khó: lẫn file `-wal`/`-shm` khi đang mở, lẫn export dở, ảnh nguồn rất nặng. Cần một file duy nhất, biết nó là gì và bản nào.

**Quyết định:**

1. **Định dạng:** một file `.zip` đặt tên `<tên project>_v<số>.zip` (số đếm tăng dần theo project, lưu ở khóa `package_seq` trong `project_meta`; chỉ tăng khi gói tạo xong). Không ghi đè file có sẵn: gặp tên trùng thì lấy số kế tiếp.
2. **Bố cục:** `ntech-package.json` là entry đầu tiên, rồi toàn bộ project dưới `project/`. Manifest ghi `formatVersion` (bố cục gói, hiện 1), `appVersion`, `schemaVersion` (database bên trong), tên project, thời điểm tạo, số gói, task mục tiêu (0019), số ảnh, phần nào đã gói, số file và tổng dung lượng. Người đọc kiểm manifest trước khi giải nén: không phải gói của app, gói có `formatVersion` hay `schemaVersion` mới hơn app biết thì từ chối kèm lý do.
3. **Luôn có** database, `master`, `thumbs`, `rejected`, `versions`. **Chọn được:** `source` (ảnh nguồn, mặc định bật vì cần để chạy lại Prepare và thường chiếm gần hết dung lượng) và `exports` (dataset đã xuất, mặc định tắt vì xuất lại được từ version). File hoặc thư mục `.partial` không bao giờ vào gói.
4. **Database vào gói bằng bản sao của SQLite online backup**, rồi đặt `journal_mode = DELETE` trên bản sao: đóng gói được khi project đang mở (Annotate giữ connection, WAL còn dữ liệu) mà vẫn nhất quán, và không có `-wal`/`-shm` trong gói.
5. **Ghi vào `<tên>.zip.partial` rồi đổi tên** (cùng quy ước của export); hủy hay lỗi thì không còn gì. Kiểm dung lượng ổ đích trước (cần tổng dung lượng gói cộng 64 MB). Ảnh và file nén sẵn dùng `NoCompression`, file khác `Fastest`. Dùng `System.IO.Compression` có sẵn, không thêm gói NuGet.
6. **Mở từ gói** (form Project có nút ở cả trạng thái có project và chưa có): đọc manifest, hỏi xác nhận kèm nội dung gói, hỏi thư mục cha, giải nén vào `<thư mục cha>/<tên project>` (phải trống hoặc chưa có), rồi mở project. Giải nén vào `<thư mục>.partial` rồi đổi tên, hủy thì không còn gì; entry có đường dẫn trèo ra ngoài thư mục (zip slip) làm cả lần giải nén bị từ chối.
7. **Ranh giới:** `IProjectPackager` ở Application, `ZipProjectPackager` ở Storage, `ProjectPackageService` điều phối với `ProjectService`, `ProjectPackageViewModel` lo giao diện (chạy nền, tiến độ, hủy).

**Hệ quả:**

- Test (18 trường hợp): tên và số gói, manifest, tùy chọn gói, bỏ qua `.partial`, không ghi đè, hủy, tiến độ, ước tính dung lượng, giải nén đúng từng byte, project đang mở và đang ghi, thư mục đã có dữ liệu, gói không phải của app, gói mới hơn, zip slip, gói thiếu database. Chạy thật trên bản sao project wire harness (không kèm ảnh nguồn): 328 ảnh, 61 đã gán, 3 class, 1 version; gói 78 MB đóng trong 7,7 giây, giải nén 1,7 giây, mở lại đúng số liệu; thêm exports là 118 MB.
- **Chưa chạy bằng tay:** các hộp chọn file/thư mục của Windows và hộp xác nhận mở gói chưa được bấm thử trong app thật (UI Automation chỉ kiểm thẻ Đóng gói hiện đúng số liệu và nút bật); chưa thử gói 3,4 GB có ảnh nguồn của project thật.
- Gói không có ảnh nguồn mở được và gán nhãn bình thường (Annotate dùng `master`), nhưng không chạy lại được các bước Prepare từ ảnh gốc; app nói điều này sau khi mở.
- `original_path` trong bảng `source_images` là đường dẫn trên máy cũ và chỉ để tham khảo; app dùng `stored_path` tương đối.
- Số thứ tự 0020 theo Data Studio; nhánh Trainer có thể đã dùng số này.
