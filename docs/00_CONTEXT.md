# Trạng thái dự án

Cập nhật: 2026-11-07

## Đã có

- Solution `NTech.DataStudio.slnx` với 6 project nguồn và 4 project test.
- Domain: hình học pixel, tile, `Annotation`, `AnnotationDrawingSession` (Box, Polygon, RotatedBox, Point), `ShapeEditing` (hit-test, di chuyển, kéo đỉnh), `SourceImage`, `ImageItem`, `LabelClass`, `DatasetVersion`, `ModelProfile`.
- Storage: `project.db` với đủ bảng, migration theo `user_version`, bố cục thư mục project, đọc model profile từ YAML.
- Application: `ProjectService`; Module 1 gồm `ImportService`, `AuditReportBuilder`, `AnalyzeService`, `CleanRules`, `CleanService`, `GeometryPlanner`, `NormalizeService`.
- Module 2 Annotate: `AnnotateService` (ghi DB trước, undo/redo riêng từng ảnh), `AnnotationEditor` (logic chuột và phím tính theo pixel ảnh, test được không cần cửa sổ), `ClassService`, `StatusRules`; `AnnotationCanvas` vẽ bằng `DrawingVisual`; màn hình Annotate đủ công cụ, panel class và nhãn, dải thumbnail có bộ lọc. Xem `docs/design/screens/ANNOTATE.md`.
- Module 3 Dataset: `SplitService` (stratify theo class, theo nhóm thư mục, tái lập theo seed), `VersionBuilder` (lọc trạng thái, gộp hoặc bỏ class, bỏ ảnh không nhãn, số liệu và cảnh báo), `DatasetService` (tạo, xóa version, preview); Domain có `Affine2D`, `RenderPlan`, `AugmentSampler`, `LabelTransformer`; `OpenCvAugmentationEngine` dựng ảnh bằng một lần warp; màn hình Dataset có preview 3×3 vẽ nhãn. Tạo version chỉ ghi config và danh sách ảnh; ảnh tăng cường sinh lúc xuất ([quyết định 0007](../decisions/0007_version-freezes-membership-and-config-not-pixels.md)). Xem `docs/design/screens/DATASET.md`.
- Module 4 Export: `DatasetValidator` (lỗi chặn xuất, cảnh báo không chặn), `ExportService` (dựng ảnh song song bằng đúng `RenderPlan` của Dataset, ghi vào `.partial` rồi đổi tên, README, `version.json`, zip), `LabelShapes` (box, polygon, OBB co về tâm cho vừa ảnh); `NTech.DataStudio.Exporters` có YOLO detect, segment, OBB, classify và COCO; bộ đọc `YoloLabelParser`, `CocoLabelParser`; màn hình Export. Xem `docs/design/screens/EXPORT.md` và [quyết định 0008](../decisions/0008_export-format-choices.md).
- Imaging: đọc header không giải nén, SHA-256, pHash, dHash, phương sai Laplacian, phơi sáng, stretch 16-bit về 8-bit, resize, tile, crop ROI.
- Desktop: Themes port từ robo-station, TopBar, màn hình Project, wizard Prepare 4 bước, Annotate, Dataset, Export.
- 8 model profile trong `profiles/` (gồm segmentation: yolo26-segment, rf-detr-segment, maskrcnn-segment).
- Build và phát hành: `scripts\build`, `package`, `deploy` (mỗi lệnh có `.ps1` và `.bat`), `installer\NTech-DataStudio.iss`. Xem README. Đã chạy thật: installer 70 MB, cài vào thư mục tạm, chạy app đã cài, gỡ sạch.

## Chưa có

- Nhập nhãn có sẵn (YOLO txt, COCO json) vào project: có bộ đọc và test vòng đọc lại, chưa có service ghép nhãn vào ảnh của project và chưa có màn hình.
- Trong Module 3: tile và crop ROI tĩnh ở bước preprocessing của version (Prepare đã có tile khi tạo master).
- Icon ứng dụng (`.iss` tự dùng `Assets/AppIcon.ico` khi file này xuất hiện), `samples/`, `tests/TestData/`.
- Ký số (code signing) cho installer: chưa có, nên Windows SmartScreen sẽ cảnh báo khi cài.
- NTech Trainer: CLI, worker và app Desktop Avalonia đã chạy thật luồng dataset → YOLO detect (YOLO26, YOLO11, YOLOv8, YOLO12 thử nghiệm) → ONNX đã kiểm chứng, trên image CPU và dataset tổng hợp. App chạy thật trên Windows và Ubuntu trong WSLg; chưa chạy trên máy Ubuntu thật, GPU hay dataset thật. Xem `handoff/trainer.md`.

## Tiêu chí nghiệm thu MVP

| Tiêu chí | Trạng thái |
| --- | --- |
| Import 1.000 ảnh BMP 20MP, giảm ≥ 10 lần | Chưa chạy đủ quy mô và chưa thử trên ảnh thật. Đã chạy 67 file 20MP tổng hợp, xem bên dưới |
| Phát hiện đúng toàn bộ ảnh trùng và gần trùng gài sẵn | Đạt trên bộ tổng hợp (`PreparePipelineTests` và phép đo 20MP). Chưa thử trên ảnh thật |
| Zoom/pan mượt trên ảnh 20MP có 200 nhãn | Đạt trên ảnh tổng hợp: 55 fps ở 20 MP và 59 fps ở 50 MP. Chưa thử ảnh thật, GPU yếu hay ảnh trên 16.384 px. Xem ANNOTATE.md |
| Gán được box, polygon, OBB có undo/redo; tắt app đột ngột không mất nhãn | Đạt trên app thật: vẽ bằng chuột, undo/redo, kill tiến trình, đọc lại DB còn đủ nhãn và `integrity_check` ok. Chưa có người dùng thật gán thử |
| 300 ảnh gốc tạo version nhân ×3; overlay nhãn trên ảnh tăng cường khớp | Đạt trên ảnh tổng hợp: test `VersionBuilder` với đúng ví dụ 300 ảnh chia 70/20/10 ×3 ra khoảng 720; test engine kiểm tra từng phép: tâm vật thể trong ảnh xuất trùng tâm nhãn dưới 1 px; xem overlay trên app thật với 48 ảnh. Chưa thử ảnh thật, 300 ảnh thật hay người dùng thật |
| Dataset YOLO26 detect/seg/obb train được ngay bằng `yolo train` không sửa tay; bản COCO train được với RF-DETR | Chưa đạt: chưa chạy `yolo train` hay RF-DETR bằng bản xuất. Đã kiểm: cấu trúc, cú pháp nhãn (mọi tọa độ trong 0..1), nhãn bám vật thể trên ảnh đã xuất, xuất hai lần ra cùng byte |
| Vòng đọc lại: nhập lại bản xuất YOLO/COCO thì nhãn giống hệt | Một phần: bộ đọc đọc lại đúng nhãn từ file đã ghi (test); chưa nhập vào project |

## Phép đo 20MP (2026-11-06)

Máy 8 luồng, RAM 16 GB. 67 file 5472 × 3648 tổng hợp có nhiễu kiểu camera, lẫn BMP, PNG, TIFF, JPEG, tổng 2,7 GB, gài 6 ảnh gần trùng và 1 file hỏng.

| Bước | Thời gian | RAM đỉnh | Kết quả |
| --- | --- | --- | --- |
| Import và Audit (chỉ header) | 0,3 s | 227 MB | 67 file, 1 hỏng |
| Phân tích | 35,9 s | 585 MB | |
| Làm sạch | dưới 0,1 s | | Loại đúng 6 ảnh gần trùng và 1 file hỏng, không loại nhầm |
| Chuẩn hóa, giữ nguyên cỡ | 27,0 s | 530 MB | 2.391 MB còn 338 MB, giảm 7,1 lần |
| Chuẩn hóa, cạnh dài 2048 | 17,4 s | 404 MB | còn 51 MB, giảm 46,1 lần |
| Tile 1280, chồng lấn 128 | 36,8 s | 283 MB | 1.200 ảnh, còn 555 MB, giảm 4,3 lần |

Bộ đo lẫn cả PNG, TIFF và JPEG vốn đã nén, nên tỉ lệ thấp hơn bộ toàn BMP. Trung bình một master JPEG giữ nguyên cỡ nặng 5,6 MB, so với 60 MB của một BMP 20MP, tức khoảng 10,7 lần; đây là phép tính, chưa đo trực tiếp trên bộ toàn BMP. Ảnh thật có thể nén khác ảnh tổng hợp. Suy ra 1.000 ảnh mất khoảng 9 phút phân tích và 5–8 phút chuẩn hóa.

Toàn bộ test Data Studio: 523 (Domain 134, Imaging 72, Exporters 23, Application và Storage 294); Trainer có thêm 100 (Trainer 64, Trainer.Desktop 36). Desktop chưa có project test; AnnotateViewModel, DatasetViewModel, ExportViewModel, AnnotationCanvas, LabelPreview và các view chỉ được kiểm tra bằng chạy app thật qua UI Automation.
