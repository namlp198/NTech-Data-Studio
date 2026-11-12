# Danh mục quyết định

| Số | Quyết định | Ngày |
| --- | --- | --- |
| [0001](0001_dotnet10-wpf-mvvm.md) | MVP viết hoàn toàn bằng .NET 10, WPF, MVVM | 2026-11-06 |
| [0002](0002_namespace-ntech-datastudio.md) | Namespace, assembly, project dùng `NTech.DataStudio.*` | 2026-11-06 |
| [0003](0003_nvision-only-nvmodel.md) | NVision chỉ nhận file `.nvmodel` | 2026-11-06 |
| [0004](0004_source-images-separate-from-masters.md) | Tách `source_images` khỏi `images` để hỗ trợ tile | 2026-11-06 |
| [0005](0005_reject-marks-never-moves.md) | Loại ảnh chỉ đánh dấu, không di chuyển file | 2026-11-06 |
| [0006](0006_annotate-saves-first-then-updates-memory.md) | Mỗi sửa nhãn ghi DB trước, rồi mới đổi bộ nhớ | 2026-11-06 |
| [0007](0007_version-freezes-membership-and-config-not-pixels.md) | Version đóng băng danh sách ảnh, split và config, không đóng băng điểm ảnh | 2026-11-07 |
| [0008](0008_export-format-choices.md) | Lựa chọn khi ghi YOLO và COCO: không `path` trong data.yaml, OBB co về tâm, tên ASCII, ghi vào .partial | 2026-11-07 |
| [0009](0009_trainer-yolo-versions-through-ultralytics.md) | Trainer bắt đầu bằng YOLO detect nhiều phiên bản qua gói `ultralytics`, kèm điều kiện license | 2026-11-08 |
| [0010](0010_trainer-workspace-and-event-log.md) | Workspace là nguồn sự thật, giao tiếp với worker bằng log sự kiện | 2026-11-08 |
| [0011](0011_trainer-runtime-in-docker.md) | Runtime của Trainer nằm trong Docker, TensorRT là tùy chọn | 2026-11-08 |
| [0012](0012_onnx-embedded-nms-and-consumer-contract.md) | ONNX dùng NMS nhúng cho mọi phiên bản, kèm hợp đồng trong `model.meta.json` | 2026-11-08 |
| [0013](0013_trainer-desktop-avalonia.md) | Desktop của Trainer viết bằng Avalonia, dùng chung use case với CLI | 2026-11-08 |
| [0014](0014_project-target-task.md) | Project khai báo task mục tiêu (detect, segment, OBB, classify); Annotate bật công cụ theo task, Export cảnh báo khi lệch | 2026-11-08 |
| [0015](0015_user-settings-and-rebindable-shortcuts.md) | Cài đặt theo người dùng (settings.json), phím tắt của Annotate đổi được, một phím một việc | 2026-11-08 |
| [0016](0016_magic-polygon-with-sam-onnx.md) | Magic polygon: tự tách vật thể bằng SAM (MobileSAM ONNX) chạy offline, model đặt ngoài installer | 2026-11-08 |
| [0017](0017_auto-label-with-trainer-segment-model.md) | Gán nhãn tự động bằng model segment do Trainer xuất (ONNX), nhãn `model` chờ duyệt từng ảnh | 2026-11-09 |
| [0018](0018_auto-label-advanced-option-and-detect-sam.md) | Gán nhãn tự động là tùy chọn nâng cao (tắt mặc định); thêm cách model detect + SAM cắt từng khung | 2026-11-11 |
| [0019](0019_project-multiple-tasks-and-auto-label-per-task.md) | Project có nhiều task mục tiêu; gán tự động theo task (Detect ra box, Segment ra polygon); Cài đặt chia mục | 2026-11-11 |
| [0020](0020_project-package-zip.md) | Đóng gói project thành file .zip có version (manifest, backup SQLite, giải nén an toàn) | 2026-11-11 |
