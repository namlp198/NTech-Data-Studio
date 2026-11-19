# 0021: Tên hiển thị đổi thành NTech Labeling Studio

Ngày: 2026-11-19

Tên "Data Studio" không nói rõ phần mềm làm gì, trong khi "Trainer" thì rõ. Phần mềm chủ yếu gán nhãn và dựng dataset, nên tên hiển thị đổi thành **NTech Labeling Studio**.

**Đổi (nhìn thấy được):** tiêu đề cửa sổ, `Product` và `AssemblyTitle`, tên trong installer (AppName, thư mục cài mặc định `NTech Labeling Studio`), tên file installer `NTech-LabelingStudio-Setup-<version>.exe`, file `installer\NTech-LabelingStudio.iss`, thư mục publish `publish\NTechLabelingStudio`, thông báo lỗi và chữ ghi vào bản xuất dataset (README, `data.yaml`, mô tả COCO), README và tài liệu.

**Không đổi:**

- Namespace, assembly, tên project `NTech.DataStudio.*` và tên file `NTech.DataStudio.Desktop.exe` (quyết định 0002; Trainer dùng chung tiền tố).
- `AppId` trong `.iss`: nhờ vậy installer mới nhận ra bản đã cài và nâng cấp. Máy đã cài bản cũ sẽ được nâng cấp vào thư mục cũ (Inno Setup dùng lại thư mục và nhóm menu đã cài), nên tên thư mục `NTech Data Studio` cũ còn ở máy đó.
- `%APPDATA%\NTech\DataStudio` (settings.json) và `%LOCALAPPDATA%\NTech\DataStudio\models`: đổi sẽ làm mất cài đặt và model SAM của người dùng.
- Định danh trong file gói project (`ntech-package.json`) và cấu trúc project: gói cũ vẫn mở được.
- Các quyết định và bản bàn giao cũ giữ nguyên chữ "Data Studio" như lịch sử; tên nhánh `dev/data-studio` và tên repo giữ nguyên.

**Hệ quả:** README trong bản xuất dataset ghi "Xuất từ NTech Labeling Studio", nên bản xuất mới khác bản cũ ở dòng này (không ảnh hưởng ảnh hay nhãn). Chạy `scripts\deploy.bat -SkipTests` và cài thử installer vào thư mục tạm để kiểm tên mới.
