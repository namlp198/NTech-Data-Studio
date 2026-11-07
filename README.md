# NTech Data Studio

Ứng dụng desktop chạy hoàn toàn offline: ảnh thô → dataset sạch → gán nhãn → tăng cường → xuất bộ dữ liệu train được ngay cho YOLO26 (và COCO cho RF-DETR).

Tên hiển thị là **NTech Data Studio**. Namespace, assembly và project là `NTech.DataStudio.*`.

## Trạng thái

Chạy được: tạo/mở project, Module 1 Prepare (import, audit, làm sạch, chuẩn hóa) và Module 2 Annotate (box, polygon, rotated box, tag, class, undo/redo, tự lưu) và Module 3 Dataset (chia train/val/test, preprocessing, tăng cường, preview, tạo version). Module 4 Export (YOLO26 detect, segment, OBB, classify; COCO; kiểm tra trước khi xuất; zip). Chưa có nhập nhãn có sẵn vào project. Xem [docs/00_CONTEXT.md](docs/00_CONTEXT.md).

## Yêu cầu

Windows và .NET SDK 10 (ghim trong `global.json`).

## Lệnh thường dùng

```powershell
dotnet build NTech.DataStudio.slnx -c Debug
dotnet test NTech.DataStudio.slnx
dotnet run --project src\NTech.DataStudio.Desktop
```

## Build và phát hành

Cần [Inno Setup 6](https://jrsoftware.org/isdl.php) để đóng gói. Version nằm ở `<Version>` trong `Directory.Build.props`; thêm `-Version x.y.z` để ghi đè. Mỗi lệnh có bản `.ps1` và bản `.bat` bọc ngoài, `.bat` nhận cùng tham số.

| Lệnh | Làm gì |
| --- | --- |
| `scripts\build.bat` | Publish Release, self-contained win-x64, ra `publish\NTechDataStudio` |
| `scripts\package.bat` | Publish rồi đóng gói Inno Setup, ra `dist\NTech-DataStudio-Setup-<version>.exe` |
| `scripts\deploy.bat` | Chạy test, đóng gói, ghi file `.sha256`; thêm `-Destination <thư mục>` để chép đi và kiểm tra lại hash |

```powershell
scripts\deploy.bat -Version 0.2.0 -Destination \\server\releases\DataStudio
```

- Test lỗi thì `deploy` dừng và không đóng gói. Bỏ qua bằng `-SkipTests`.
- `deploy` không ghi đè installer cùng tên nhưng khác nội dung ở thư mục đích. Hãy tăng version, hoặc thêm `-Force` nếu chủ ý thay.
- Cài lên máy không có quyền admin: chạy installer với `/CURRENTUSER`.
- Gỡ cài đặt chỉ xóa file chương trình. Project của người dùng nằm ngoài thư mục cài nên không bị đụng tới.

## Bắt đầu đọc từ đâu

- [Tài liệu thiết kế](docs/DESIGN_DOC.md)
- [Kiến trúc](docs/architecture/ARCHITECTURE.md), [mô hình dữ liệu](docs/architecture/DATA_MODEL.md), [pipeline](docs/architecture/PIPELINE.md)
- [Design system](docs/design/DESIGN_SYSTEM.md), [UI guidelines](docs/design/UI_GUIDELINES.md)
- Màn hình: [Prepare](docs/design/screens/PREPARE.md), [Annotate](docs/design/screens/ANNOTATE.md), [Dataset](docs/design/screens/DATASET.md), [Export](docs/design/screens/EXPORT.md)
- [Thiết kế NTech Trainer](docs/trainer/TRAINER_SYSTEM_DESIGN.md) (bản nháp, chưa có code)
- [Quyết định kiến trúc](decisions/0000_INDEX.md)
- [Quy trình làm việc với AI](docs/README_AI_WORKFLOW.md)
