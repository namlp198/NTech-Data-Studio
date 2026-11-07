# 0013: Desktop của Trainer viết bằng Avalonia, dùng chung use case với CLI

Ngày: 2026-11-08

Trainer phải chạy trên cả Windows và Ubuntu (máy train là Linux hoặc Windows cấu hình cao). WPF của Data Studio chỉ chạy trên Windows.

**Quyết định:**

1. `NTech.DataStudio.Trainer.Desktop` dùng Avalonia 12 với Fluent theme và CommunityToolkit.Mvvm. WPF Data Studio giữ nguyên, không viết lại.
2. Desktop và CLI gọi cùng use case trong `NTech.DataStudio.Trainer`: `DatasetChecker`, `TrainPlanner`, `RunMonitor`, `EventText`, `RunSummary`, `RunStateTracker`. CLI không còn giữ logic riêng ngoài phần in ra màn hình.
3. ViewModel không dùng kiểu nào của Avalonia: hộp chọn file, mở thư mục và nơi lưu thiết lập đi qua `IDialogs` và `ISettingsStore`. Màn hình theo dõi đọc lại file trong workspace mỗi giây, chỉ khi đang hiện; đóng app không dừng job.
4. Token của WPF được port sang `Themes/Tokens/*.axaml`, giữ nguyên tên khóa và giá trị. `TokenParityTests` so từng giá trị chung. Token chỉ của Trainer thêm tên mới, ví dụ `Color.Grade.Weak` cho mức Weak màu cam.
5. Font chính là Segoe UI, sau đó tới Inter đóng gói kèm app, vì Ubuntu không có Segoe UI và Inter có đủ dấu tiếng Việt.

**Hệ quả:**

- Test giao diện chạy bằng `Avalonia.Headless`, gọi `HeadlessUnitTestSession` trực tiếp vì `Avalonia.Headless.XUnit` cần xunit v3, còn repo đang dùng xunit 2. Mỗi màn hình được dựng với dữ liệu trên đĩa, lưu ảnh PNG và kiểm tra không có lỗi binding.
- Trên Linux, Avalonia cần thư viện X11 của hệ điều hành; Ubuntu desktop thường đã có, WSL Ubuntu thiếu `libice6`, `libsm6`. Gói Ubuntu ở M4 phải khai báo các phụ thuộc này.
- Thêm gói Avalonia vào `Directory.Packages.props`, file dùng chung với Data Studio. Bên merge sau xử lý nếu trùng dòng.
