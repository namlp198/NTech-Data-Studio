> Bản lưu trong repo của tài liệu thiết kế. Khác bản gốc ở một điểm: namespace, assembly và project đổi từ tên cũ sang `NTech.DataStudio.*` theo [quyết định 0002](../decisions/0002_namespace-ntech-datastudio.md).

# Tài liệu thiết kế NTech Data Studio

Nov 6, 2026 · @ndev

## Mục tiêu và phạm vi MVP

Làm ra một ứng dụng desktop chạy hoàn toàn offline. Ứng dụng đi trọn vòng: ảnh thô → dataset sạch → gán nhãn → tăng cường → xuất bộ dữ liệu train được ngay cho YOLO26 (và COCO cho RF-DETR). Không upload ảnh lên đâu, không giới hạn dung lượng, ảnh giữ gần như nguyên chất lượng.

**Trong MVP:**

1. **Chuẩn bị dữ liệu:** import, audit, lọc trùng/mờ/hỏng, chuẩn hóa định dạng, resize hoặc tile theo model profile.
2. **Gán nhãn:** bounding box, polygon, rotated box và nhãn phân loại; quản lý class; undo/redo; phím tắt; zoom ảnh lớn.
3. **Chia tập và tăng cường ảnh:** chia train/val/test, sau đó sinh N bản tăng cường cho tập train, kiểu Roboflow (ví dụ 300 ảnh gốc × 3 → khoảng 1.000 ảnh).
4. **Xuất dataset:** YOLO26 (detect, segment, OBB, classify) và COCO JSON, có kiểm tra tính hợp lệ trước khi xuất.

**Để sau MVP:** gán nhãn tự động bằng model (pre-label) và SAM, Trainer tích hợp, nhiều người dùng, keypoint/pose, video.

MVP là một sản phẩm mới, đặt trong repo riêng. Nó chỉ mượn pattern và code từ robo-station, không sửa gì trong robo-station.

**Ranh giới sản phẩm:**

- **NTech Data Studio** gồm công cụ dữ liệu (MVP này) và **Trainer** (sau MVP). Hai phần phát hành cùng nhau trong một sản phẩm.
- **NVision** là sản phẩm riêng. NVision chỉ nhận file `.nvmodel` do Trainer xuất ra; không đọc dataset và không gọi trực tiếp vào Data Studio. Đặc tả `.nvmodel` sẽ viết khi làm Trainer.
- **Namespace:** toàn bộ code dùng `NTech.DataStudio.*`, kể cả Trainer (`NTech.DataStudio.Trainer`). Không dùng `NTech.NVision.*`.

## Kiến trúc và công nghệ

Quyết định: viết MVP hoàn toàn bằng **.NET 10 (LTS) + WPF + MVVM**, giống robo-station. MVP không cần Python, vì cả 4 chức năng đều là xử lý ảnh và ghi file, không có bước train. Python chỉ vào dự án ở giai đoạn Trainer, chạy thành process riêng. Mọi project đặt `TargetFramework` là `net10.0` (riêng Desktop là `net10.0-windows`), khai báo chung trong `Directory.Build.props`. robo-station đang chạy .NET 8; code copy sang từ đó biên dịch được trên .NET 10 mà không phải sửa gì đáng kể.

Lý do chọn .NET:

- Dùng lại được pattern Geo Measure, design token và cấu trúc MVVM đã chạy ổn.
- Không phải đóng gói Python/torch, bộ cài nhẹ hơn nhiều so với bản kèm torch.
- Bán thương mại dễ hơn: có thể obfuscate và gắn license key.

| Lớp | Thư viện | License | Vai trò |
| --- | --- | --- | --- |
| UI | WPF, CommunityToolkit.Mvvm | MIT | Giao diện, binding, ICommand |
| Xử lý ảnh | OpenCvSharp4 | Apache 2.0 | Đọc BMP/TIFF/PNG/JPEG kể cả 16-bit; resize; tile; tăng cường ảnh |
| Lưu trữ | Microsoft.Data.Sqlite | MIT | Mỗi project một file `project.db` |
| Config | YamlDotNet | MIT | Model profile, `data.yaml` |
| Test | xUnit | Apache 2.0 | Domain, augment, exporter |

Không dùng ImageSharp: bản mới dùng Six Labors Split License, doanh nghiệp có doanh thu lớn phải trả phí.

Solution chia lớp theo đúng hướng phụ thuộc của robo-station (`Desktop → Application → Domain`, Infrastructure hiện thực các abstraction của Application):

Cấu trúc repo đầy đủ. Vị trí đặt repo trên ổ đĩa chưa chốt. Quy trình `docs/`, `decisions/`, `tasks/`, `handoff/` theo mẫu robo-station.

```
ntech-data-studio/
├─ NTech.DataStudio.slnx
├─ global.json                       ghim SDK .NET 10
├─ Directory.Build.props             net10.0, Nullable, TreatWarningsAsErrors
├─ Directory.Packages.props          quản lý version NuGet tập trung
├─ .editorconfig  .gitignore  .gitattributes
├─ README.md
├─ CLAUDE.md                         quy tắc cho AI coding (theo mẫu robo-station)
├─ AGENTS.md
│
├─ docs/
│  ├─ 00_CONTEXT.md                  trạng thái dự án hiện tại
│  ├─ README_AI_WORKFLOW.md
│  ├─ DESIGN_DOC.md                  link tới tài liệu thiết kế này
│  ├─ architecture/
│  │  ├─ ARCHITECTURE.md
│  │  ├─ DATA_MODEL.md               bảng SQLite, thư mục project
│  │  └─ PIPELINE.md                 prep → annotate → split → augment → export
│  ├─ design/
│  │  ├─ DESIGN_SYSTEM.md
│  │  ├─ UI_GUIDELINES.md
│  │  └─ screens/                    PREPARE.md, ANNOTATE.md, DATASET.md, EXPORT.md
│  ├─ formats/
│  │  ├─ YOLO26.md
│  │  ├─ COCO.md
│  │  └─ NVMODEL.md                  viết khi làm Trainer
│  └─ product/
│     └─ SCREEN_REQUIREMENTS.md
├─ decisions/
│  ├─ 0000_INDEX.md
│  ├─ 0001_dotnet10-wpf-mvvm.md
│  ├─ 0002_namespace-ntech-datastudio.md
│  └─ 0003_nvision-only-nvmodel.md
├─ tasks/
│  ├─ active.md
│  └─ backlog.md
├─ handoff/
│  └─ latest.md
│
├─ profiles/                         model profile YAML, đóng gói kèm bản cài
│  ├─ yolo26-detect.yaml   yolo26-segment.yaml   yolo26-obb.yaml   yolo26-classify.yaml
│  ├─ rf-detr-detect.yaml  rf-detr-segment.yaml
│  └─ anomaly-patchcore.yaml
│
├─ src/
│  ├─ NTech.DataStudio.Domain/
│  │  ├─ Geometry/        PixelPoint, PixelRect, RotatedRect, PolygonMath
│  │  ├─ Annotations/     Annotation, ShapeKind, AnnotationDrawingSession
│  │  ├─ Images/          ImageItem, ImageStatus
│  │  ├─ Classes/         LabelClass
│  │  ├─ Datasets/        DatasetVersion, SplitKind, SplitAssignment
│  │  └─ Profiles/        ModelProfile, TaskKind
│  ├─ NTech.DataStudio.Application/
│  │  ├─ Abstractions/    IImageCodec, IProjectStore, IDatasetExporter, IAugmentationEngine
│  │  ├─ Projects/        tạo, mở, đóng project
│  │  ├─ Prepare/         Import, Audit, Clean, Normalize, GeometryPlanner
│  │  ├─ Annotate/        AnnotationService, lệnh Undo/Redo
│  │  ├─ Datasets/        SplitService, VersionBuilder
│  │  ├─ Augment/         AugmentationPlan, AugmentationOptions
│  │  ├─ Export/          ExportService, DatasetValidator
│  │  └─ Import/          YoloLabelImporter, CocoLabelImporter
│  ├─ NTech.DataStudio.Imaging/
│  │  ├─ Codecs/          OpenCvImageCodec (BMP/TIFF/PNG/JPEG, 16-bit)
│  │  ├─ Quality/         HashService (SHA-256, dHash, pHash), BlurDetector, ExposureAnalyzer
│  │  ├─ Transforms/      Resize, Tile, RoiCrop, BitDepth, ColorSpace
│  │  └─ Augment/         AffineAugmenter, PhotometricAugmenter, LabelTransformer
│  ├─ NTech.DataStudio.Storage/
│  │  ├─ Sqlite/          ProjectDb, Migrations/
│  │  └─ Files/           ProjectFolderLayout
│  ├─ NTech.DataStudio.Exporters/
│  │  ├─ Yolo/            YoloDetect-, YoloSegment-, YoloObb-, YoloClassifyExporter
│  │  └─ Coco/            CocoExporter
│  └─ NTech.DataStudio.Desktop/      net10.0-windows, WPF
│     ├─ App.xaml, App.xaml.cs
│     ├─ Bootstrap/       AppComposition.cs (composition root thủ công, như robo-station)
│     ├─ Shell/           MainWindow, TopBar, ViewSelector, StatusIconStrip
│     ├─ Views/           Project/, Prepare/, Annotate/, Dataset/, Export/
│     ├─ ViewModels/      cùng cấu trúc với Views/
│     ├─ Controls/        AnnotationCanvas, ToolRail, ClassPanel, ThumbnailStrip
│     ├─ Converters/
│     ├─ Themes/          Tokens/, Components/, DarkTheme.xaml
│     └─ Assets/          Icons/, AppIcon.ico
│
├─ tests/
│  ├─ NTech.DataStudio.Domain.Tests/
│  ├─ NTech.DataStudio.Application.Tests/
│  ├─ NTech.DataStudio.Imaging.Tests/       kiểm tra biến đổi nhãn khi augment
│  ├─ NTech.DataStudio.Exporters.Tests/     so với file mẫu chuẩn, round-trip
│  └─ TestData/                               ảnh và nhãn mẫu nhỏ
├─ samples/                          project mẫu để demo
├─ scripts/
│  ├─ build.ps1
│  ├─ test.ps1
│  └─ package.ps1
└─ installer/

# Sau MVP:
#   src/NTech.DataStudio.Trainer/      C#: gọi process Python, theo dõi train, đóng gói .nvmodel
#   trainer/python/                      PyTorch, RF-DETR, script train/export
```

Tên sản phẩm hiển thị là **NTech Data Studio**, còn namespace, tên assembly và tên project là `NTech.DataStudio.*`. Sau MVP, Trainer được thêm vào cùng solution dưới dạng `src/NTech.DataStudio.Trainer` (phần C#: gọi process Python, theo dõi train, đóng gói `.nvmodel`).

## Tái sử dụng Geo Measure

Geo Measure (khoảng 2.450 dòng) dùng lại được **mô hình tương tác và cấu trúc code**, nhưng phần hình học phải viết lại. Code hiện tại làm việc trên tọa độ WGS84 và vẽ trên GMap.NET, còn annotator làm việc trên tọa độ pixel của ảnh.

| Thành phần trong robo-station | Cách dùng trong NTech Data Studio |
| --- | --- |
| `GeoDrawingSession` (state machine vẽ 1 hình: vertices, candidate, `CanFinish`, kiểm tra tự cắt) | Port sang `AnnotationDrawingSession` dùng `PixelPoint(X, Y)`. Giữ nguyên logic Line/Polygon/Rectangle, bỏ Circle, thêm RotatedBox |
| `GeoMeasureShapeType`, `GeoMeasureFeature` | Đổi thành `ShapeKind` (Box, Polygon, RotatedBox, Point) và `Annotation` (Id, ClassId, Points, Source, CreatedAt) |
| `GeoMeasurementCalculator.HasSelfIntersection`, `RectangleCorners` | Giữ thuật toán, đổi từ geodesic sang hình học phẳng (đơn giản hơn) |
| `GeoMeasureViewModel`: Select tool, Finish, Undo point, Cancel, Delete, Zoom-to | Giữ bộ command và luồng; thêm Undo/Redo toàn cục, Next/Prev image, chọn class |
| Code-behind: mouse down/move/up, capture, Space-pan, Esc/Enter/Backspace | Giữ nguyên cách bắt phím và chuột; thay `GMapControl` bằng canvas ảnh có zoom/pan |
| Layout: tool rail trái, strip Finish/Undo/Cancel, Features panel phải | Dùng lại gần nguyên: rail = công cụ vẽ, panel phải = danh sách nhãn của ảnh và class |
| `Themes/Tokens` và `Themes/Components` (Buttons, Inputs, DataGrid, Overlay…) | Copy sang để đồng bộ giao diện NTech; giữ pattern self-merge `_Tokens.xaml` |
| Test xUnit của Calculator và ViewModel | Làm mẫu cho test của Domain và ViewModel mới |

Không dùng lại: GMap.NET, tile provider, tìm địa điểm, vị trí robot/RTK, gateway.

Cách làm: **copy có chọn lọc sang repo mới**. Không tách thư viện dùng chung ngay, vì nếu tách thì phải sửa robo-station, mà robo-station có quy trình duyệt kiến trúc riêng. Khi cả hai sản phẩm đã ổn mới cân nhắc tách `NTech.UI.Themes` thành package chung.

## Design system (theo robo-station)

NTech Data Studio dùng **chung ngôn ngữ thiết kế với robo-station**: giao diện tối kiểu "modern industrial control", canvas chiếm toàn màn hình, công cụ và panel nổi trên canvas, mọi màu và kích thước lấy từ token. Bước đầu tiên là copy nguyên `Themes/` của robo-station sang, rồi viết 2 file làm chuẩn cho repo mới:

- `docs/design/DESIGN_SYSTEM.md`: bảng token (màu, chữ, khoảng cách, bo góc, kích thước), component dùng chung và trạng thái.
- `docs/design/UI_GUIDELINES.md`: bố cục, hành vi, những điều cấm. Rút gọn từ `UI_GUIDELINES.md` của robo-station, bỏ phần robot/mission.

```
src/NTech.DataStudio.Desktop/Themes/
  Tokens/_Tokens.xaml        gộp mọi token (giữ pattern self-merge)
  Tokens/Colors.xaml         copy từ robo-station, bỏ nhóm Mission/Robot/Zone/Fleet
  Tokens/Typography.xaml     copy nguyên
  Tokens/Spacing.xaml        copy nguyên (bỏ Fence.*)
  Tokens/Sizing.xaml         giữ ControlHeight, IconSize, Layout.TopBar/ToolRail/FloatingPanel
  Tokens/Elevation.xaml      copy nguyên
  Tokens/Icons.xaml          giữ icon chung, thêm icon Box/Polygon/OBB/Tag
  Tokens/Annotation.xaml     MỚI: màu class, nét vẽ, handle, trạng thái ảnh
  Components/                Buttons, Inputs, InputControls, DataGrid, Menus, Overlay, ScrollBars, Text
  DarkTheme.xaml
```

**Màu nền tảng** (giữ nguyên giá trị của robo-station):

| Token | Giá trị | Dùng cho |
| --- | --- | --- |
| `Color.Background.App` | #0B1220 | Nền ứng dụng, vùng quanh ảnh trên canvas |
| `Color.Background.Surface` | #111A2B | Panel, TopBar, tool rail |
| `Color.Background.Elevated` | #172236 | Popup, dialog, menu |
| `Color.Background.Hover` / `Selected` | #1C2A42 / #203A5E | Hover và dòng/thumbnail đang chọn |
| `Color.Border.Default` / `Strong` | #29364C / #3B4A63 | Viền panel, ô nhập |
| `Color.Text.Primary` / `Secondary` / `Muted` / `Disabled` | #F3F6FA / #AAB5C5 / #718096 / #566276 | Chữ |
| `Color.Brand.Primary` (Hover / Pressed) | #2F80ED (#4A92F0 / #1F6FD0) | Nút chính, công cụ đang bật, nhãn đang chọn |
| `Color.Brand.Secondary` | #00A6A6 | Nhấn phụ |
| `Color.Status.Success` / `Warning` / `Error` / `Info` / `Neutral` | #2DBE78 / #F2B84B / #E45858 / #4C9AFF / #7F8A9A | Trạng thái, cảnh báo validator |

**Token mới cho gán nhãn** (`Annotation.xaml`):

| Token | Giá trị | Dùng cho |
| --- | --- | --- |
| `Color.Class.1` … `Color.Class.10` | lấy lần lượt từ `Color.Fleet.Trajectory1…10` (#4A90E2, #E25D5D, #34C3A0, #C77DFF, #FF9F43, #2EC4E6, #F05FB8, #9BC53D, #FFD166, #7E8CE0) | Màu mặc định của class thứ 1–10; từ class 11 trở đi xoay vòng, người dùng đổi được |
| `Annotation.Stroke` / `StrokeSelected` | 1.5 / 2.5 px màn hình | Nét viền nhãn, không đổi khi zoom |
| `Annotation.FillOpacity` / `FillOpacitySelected` | 0.16 / 0.28 | Màu nền trong nhãn (theo pattern `Zone.FillOpacity` 0.16) |
| `Annotation.HandleSize` | 8 px | Ô vuông kéo đỉnh, nền `Text.Primary`, viền `Brand.Primary` |
| `Annotation.Crosshair` | `Text.Secondary`, opacity 0.6 | Đường gióng khi vẽ box |
| Trạng thái ảnh New / Labeled / Reviewed / Null / Rejected | `Status.Neutral` / `Info` / `Success` / `Neutral` (viền đứt) / `Error` | Chấm trạng thái trên thumbnail |

**Chữ, khoảng cách, kích thước** (copy nguyên): font Segoe UI và Cascadia Mono (số liệu, toạ độ, kích thước ảnh). Cỡ chữ Display 28, H1 24, H2 20, H3 16, Body 14, BodySmall 13, Caption 12, Micro 11. Khoảng cách 4 / 8 / 12 / 16 / 24 / 32. Bo góc 4 / 8 / 12. Control cao 28 / 36 / 44, icon 14 / 18 / 24. TopBar 48, tool rail 56, floating panel rộng 320.

**Bố cục NTech Data Studio** theo HUD shell của robo-station:

- **TopBar 48 px:** bên trái là ViewSelector gồm Project · Prepare · Annotate · Dataset · Export; bên phải là StatusIconStrip gồm tên project, số ảnh đã gán / tổng số, cảnh báo validator.
- **Annotate:** canvas ảnh full-bleed. Tool rail 56 px nổi bên trái (Select, Box, Polygon, OBB, Tag, Zoom, Fit). Floating panel 320 px bên phải chứa Class và Nhãn. Dải thumbnail nổi ở đáy. Strip Finish/Undo/Cancel giống hệt Geo Measure.
- **Prepare, Dataset, Export:** đây là màn hình form/bảng, nên dùng layout trang (PageHeader, nội dung, primary action) như mục 3.6 trong guideline của robo-station.

**Quy tắc bắt buộc** (giữ từ robo-station):

- View không hard-code màu hay khoảng cách; chỉ dùng `StaticResource` từ `Themes/`.
- Logic nằm trong ViewModel, dùng binding và `ICommand`. Code-behind chỉ để xử lý chuột và vẽ trên canvas.
- Mỗi màn hình có đủ các trạng thái loading, empty, error.
- Không dùng emoji thay icon, không gradient, không shadow đậm, không bo góc lớn.
- Màu chỉ dùng khi có ý nghĩa: Brand cho hành động; Status cho trạng thái; màu class chỉ dùng cho nhãn.

## Cấu trúc project và mô hình dữ liệu

Mỗi project là một thư mục tự chứa: copy thư mục đó sang máy khác là mở được ngay. Nguyên tắc cốt lõi: **ảnh gốc không bao giờ bị sửa**. Nhãn gắn với ảnh master, còn ảnh tăng cường và dataset xuất ra luôn sinh lại được từ master và config.

```
MyProject/
  project.db            SQLite: ảnh, class, nhãn, split, version, lịch sử
  source/               (tùy chọn) link hoặc copy ảnh gốc, không sửa
  master/               ảnh đã chuẩn hóa, dùng để gán nhãn
  thumbs/               thumbnail 256 px cho lưới ảnh
  rejected/             ảnh bị loại, kèm lý do
  versions/v001/        snapshot: config prep + split + augment
  exports/v001_yolo26_detect/
```

| Bảng | Trường chính |
| --- | --- |
| `images` | id, source\_path, master\_path, width, height, sha256, phash, status (new / labeled / reviewed / rejected / null), group\_key |
| `classes` | id, name, color, hotkey, order |
| `annotations` | id, image\_id, class\_id, kind (box / polygon / obb / point), points\_json (pixel trên master), source (manual / model), created\_at, updated\_at |
| `image_tags` | image\_id, class\_id (nhãn classification) |
| `versions` | id, name, prep\_json, split\_json, augment\_json, seed, created\_at |
| `version_items` | version\_id, image\_id, split (train / val / test) |

Nhãn lưu bằng tọa độ pixel trên ảnh master. Exporter tự chuẩn hóa về 0..1 theo yêu cầu từng format. Version có `seed` cố định, nên cùng một version luôn sinh ra cùng một bộ ảnh tăng cường.

## Module 1: Chuẩn bị dữ liệu sạch

Module 1 biến một thư mục ảnh lẫn lộn thành bộ ảnh master đồng nhất, nhẹ, sẵn sàng gán nhãn. Ví dụ một ảnh BMP 20MP nặng 58 MB sẽ thành một file JPEG hoặc PNG vài MB. Màn hình gồm wizard 4 bước và preview trước/sau.

1. **Import:** kéo thả thư mục, quét đệ quy. Hỗ trợ BMP, TIFF, PNG, JPEG, WebP. Chọn copy ảnh vào project hay chỉ tham chiếu đường dẫn.
2. **Audit:** chỉ đọc header nên nhanh. Hiển thị phân bố độ phân giải, định dạng, bit depth, số kênh, dung lượng, file hỏng. Ước tính dung lượng sau xử lý.
3. **Làm sạch:** các ngưỡng chỉnh được, kết quả đưa vào `rejected/` cho người dùng duyệt, không xóa.
   - Trùng hoàn toàn: SHA-256
   - Gần trùng: dHash/pHash, khoảng cách Hamming nhỏ hơn hoặc bằng ngưỡng (mặc định 5)
   - Mờ: độ lệch của Laplacian dưới ngưỡng (tự gợi ý theo phân vị thứ 5 của bộ ảnh)
   - Quá tối hoặc cháy sáng: dựa trên histogram
4. **Chuẩn hóa và hình học:**
   - Xoay theo EXIF rồi bỏ metadata. Ảnh 16-bit chuyển về 8-bit bằng percentile stretch. Đưa về sRGB, bỏ kênh alpha. Ảnh mono giữ 1 kênh hoặc nhân thành 3 kênh.
   - Chọn **model profile** (YOLO26, RF-DETR, Mask R-CNN, Classification, Anomaly) và nhập **kích thước vật thể nhỏ nhất** (pixel trên ảnh gốc). Tool tính và đề xuất một trong ba cách:
     - **Downscale:** cạnh dài master tối đa 2048 hoặc 4096
     - **Tile:** kích thước tile và độ chồng lấn
     - **Crop ROI:** vẽ 1 vùng cố định, áp cho mọi ảnh
   - Hiển thị cảnh báo kiểu: “Ở imgsz 640, vật thể 40 px còn 5 px. Nên dùng imgsz 1280 hoặc tile.”
   - Định dạng master: JPEG q95 mặc định, PNG khi cần lossless (lỗi bề mặt rất mảnh).

Model profile là file YAML trong thư mục `profiles/`. Thêm model mới chỉ cần thêm file, không phải sửa code:

```yaml
id: yolo26-detect
family: ultralytics-yolo
task: detect
imgsz_default: 640
imgsz_options: [640, 960, 1280]
stride: 32
min_object_px: 12
export_formats: [yolo]
```

Xử lý chạy song song bằng `Parallel.ForEach`, giới hạn số luồng theo RAM vì mỗi ảnh 20MP giải nén ra chiếm khoảng 60 MB bộ nhớ. Có thanh tiến độ và nút Hủy. Chạy lại thì tự bỏ qua ảnh đã xử lý (so theo hash).

## Module 2: Gán nhãn

Giao diện gán nhãn theo mô hình Roboflow Annotate, dựng trên khung Geo Measure: tool rail bên trái, canvas ảnh ở giữa, bên phải là panel Nhãn/Class, dưới cùng là dải thumbnail. Mục tiêu tốc độ: một người gán được 10 box trên một ảnh mà không cần rời bàn phím và chuột.

**Công cụ vẽ và sửa (MVP):**

- Bounding box: kéo 2 góc; có đường crosshair toàn ảnh để căn mép.
- Polygon: click từng đỉnh. Enter để kết thúc, Backspace để bỏ đỉnh vừa vẽ, Esc để hủy. Đây là phím tắt có sẵn trong Geo Measure. Có kiểm tra tự cắt.
- Rotated box (OBB): kéo trục chính rồi kéo bề rộng, hoặc xoay box bằng tay cầm.
- Nhãn phân loại cả ảnh (tag): dùng cho tác vụ classify.
- Sửa hình: kéo đỉnh hoặc cạnh; double-click lên cạnh polygon để thêm đỉnh; Delete để xóa; kéo cả hình để di chuyển; Ctrl+C/V để sao chép trong một ảnh và sang ảnh khác.
- **Copy nhãn từ ảnh trước:** rất hữu ích với camera tĩnh, khi các ảnh liên tiếp gần như giống nhau.
- Đánh dấu ảnh **Null / Background**: ảnh không có vật thể, vẫn được xuất ra để giảm nhận nhầm.

**Class và điều hướng:**

- Quản lý class: thêm, đổi tên, đổi màu, gộp, xóa (có cảnh báo số nhãn bị ảnh hưởng). Phím 1–9 chọn class; mặc định dùng lại class vừa chọn.
- Panel nhãn: danh sách theo class, ẩn/hiện, khóa, chỉnh độ trong suốt, bấm vào để zoom tới nhãn.
- Điều hướng: A/D hoặc ←/→ sang ảnh trước/sau; lọc theo trạng thái (chưa gán / đã gán / null / cần review) và theo class.
- Trạng thái ảnh: New → Labeled → Reviewed. Có chế độ Review để duyệt hoặc trả lại từng ảnh.

**Kỹ thuật:**

- Zoom và pan ảnh 20MP: Ảnh hiển thị đầy đủ độ phân giải trong một `ScaleTransform` + `TranslateTransform`. Cuộn chuột zoom quanh vị trí con trỏ, giống `ApplyDisplayZoom(anchor)` của Geo Measure. Giữ Space để pan. Tải trước ảnh kế tiếp ở nền.
- Lớp nhãn vẽ bằng `DrawingVisual` (không dùng mỗi nhãn một `Shape`) để mượt khi có hàng trăm nhãn. Độ dày nét giữ cố định theo pixel màn hình dù zoom ở mức nào.
- Undo/Redo dùng command pattern (Add/Move/Edit/Delete), stack riêng cho từng ảnh.
- Tự động lưu vào SQLite sau mỗi thao tác, không có nút Save, nên không mất dữ liệu khi mất điện.
- Hit-test và snap làm trong tọa độ pixel của ảnh. Nhãn được kẹp (clamp) trong biên ảnh.

| Phím | Chức năng |
| --- | --- |
| B / P / R / V | Box / Polygon / Rotated box / Chọn |
| 1–9 | Chọn class |
| A, D | Ảnh trước / sau |
| Enter, Backspace, Esc | Kết thúc / bỏ đỉnh vừa vẽ / hủy |
| Ctrl+Z, Ctrl+Y | Undo / Redo |
| Space + kéo, cuộn chuột | Pan / Zoom |
| N | Đánh dấu Null |
| H | Ẩn/hiện tất cả nhãn |

## Module 3: Chia tập và tăng cường ảnh

Module 3 tạo một **Version** giống Roboflow, theo thứ tự: chia tập → preprocessing → augmentation. Augmentation **chỉ áp cho tập train**. Ví dụ 300 ảnh gốc, chia 70/20/10 và nhân ×3 cho train, sẽ ra 630 ảnh train + 60 val + 30 test = 720 ảnh.

**1. Chia tập (split)**

- Tỉ lệ mặc định 70/20/10, chỉnh được. Seed cố định.
- Stratify theo class: mỗi class đều có mặt ở cả train và val.
- Chia theo nhóm `group_key` (thư mục gốc, phiên chụp, mã sản phẩm): ảnh cùng một vật không bị rải sang cả train lẫn val, tránh metric đẹp giả.
- Hiển thị bảng số ảnh và số nhãn theo class ở mỗi tập; cảnh báo khi một class quá ít nhãn.

**2. Preprocessing (áp cho mọi tập)**

- Resize: Stretch, Fit (letterbox) hoặc Fill (crop) về kích thước đích. Hoặc giữ nguyên master và để framework tự resize (khuyến nghị cho YOLO26).
- Tile, kèm cắt nhãn theo tile; static crop theo ROI; grayscale.
- Remap hoặc bỏ class (ví dụ gộp `connector_red` và `connector_blue` thành `connector`); bỏ ảnh null theo tỉ lệ.

**3. Augmentation (chỉ tập train)**

| Nhóm | Phép biến đổi | Tham số | Ảnh hưởng tới nhãn |
| --- | --- | --- | --- |
| Hình học | Lật ngang/dọc | bật/tắt | Biến đổi toạ độ |
| Hình học | Xoay 90° | CW / CCW / 180 | Biến đổi toạ độ |
| Hình học | Xoay nhỏ | ±θ° | Box phình ra; polygon và OBB chính xác |
| Hình học | Crop/zoom, shear, scale | % | Cắt nhãn theo biên, bỏ nhãn còn dưới 30% diện tích |
| Ánh sáng | Brightness, exposure, contrast, gamma | ±% | Không |
| Màu | Hue, saturation, grayscale ngẫu nhiên | ±°, ±%, % ảnh | Không |
| Nhiễu | Gaussian blur, motion blur, noise, JPEG artifact | px, % | Không |
| Che khuất | Cutout | số ô, kích thước | Không (tuỳ chọn bỏ nhãn bị che nhiều) |

- **Output multiplier** từ ×2 đến ×10. Mỗi bản sao bốc ngẫu nhiên tham số trong khoảng đã chọn. Ảnh gốc luôn được giữ.
- **Preview lưới 3×3** có vẽ nhãn trước khi sinh, để bắt lỗi nhãn lệch ngay.
- Cài đặt: mỗi phép biến đổi hình học là một ma trận affine 3×3. Các phép được gộp thành một ma trận, chạy `warpAffine` một lần, rồi áp cùng ma trận lên điểm của nhãn. Polygon được cắt theo biên ảnh bằng Sutherland–Hodgman; OBB được fit lại bằng `minAreaRect`.

**Lưu ý quan trọng với YOLO26:** Ultralytics đã tự augment online khi train (mosaic, HSV, flip, scale, translate). Augment offline quá nhiều sẽ chồng hai lần và làm ảnh lệch khỏi thực tế. Mặc định khi chọn profile YOLO26: nhân ×2–×3, ưu tiên các biến đổi theo điều kiện thực tế (ánh sáng nhà máy, blur, nhiễu), không tạo mosaic offline. Profile RF-DETR và Anomaly có mặc định riêng.

## Module 4: Xuất dataset

Người dùng chọn một Version và một model profile, tool xuất ra thư mục train được ngay, không phải sửa tay. Mỗi exporter là một class hiện thực `IDatasetExporter`, nên thêm format mới không đụng tới code cũ.

| Profile | Format | Cấu trúc | Mỗi dòng nhãn |
| --- | --- | --- | --- |
| YOLO26 Detect | YOLO txt | `images/{train,val,test}`, `labels/{…}`, `data.yaml` | `cls cx cy w h` (chuẩn hóa 0..1) |
| YOLO26 Segment | YOLO txt | như trên | `cls x1 y1 x2 y2 …` (polygon chuẩn hóa) |
| YOLO26 OBB | YOLO txt | như trên | `cls x1 y1 x2 y2 x3 y3 x4 y4` |
| YOLO26 Classify | thư mục | `train/<class>/*.jpg`, `val/<class>/…` | (không có file nhãn) |
| RF-DETR / RT-DETR | COCO JSON | `train/`, `valid/`, `test/`, mỗi thư mục có `_annotations.coco.json` | `bbox [x, y, w, h]` pixel, `segmentation` |

Ví dụ `data.yaml` sinh ra cho YOLO26:

```yaml
path: D:/datasets/harness_v001_yolo26_detect
train: images/train
val: images/val
test: images/test
names:
  0: connector
  1: tape
  2: cable_tie
```

**Kiểm tra trước khi xuất.** Có lỗi thì chặn xuất; chỉ có cảnh báo thì cho xuất tiếp.

- Lỗi: box diện tích bằng 0 hoặc nằm ngoài ảnh, polygon dưới 3 đỉnh hoặc tự cắt, class id không tồn tại, profile Segment nhưng ảnh chỉ có box.
- Cảnh báo: class có dưới 50 nhãn, val thiếu một class, ảnh chưa gán mà không đánh dấu Null, vật thể nhỏ hơn `min_object_px` ở imgsz đã chọn.

**Kèm theo mỗi bản xuất:**

- `README.dataset.md`: số ảnh, số nhãn theo class và theo tập, các bước preprocessing và augmentation.
- `version.json`: config và seed, để tái tạo đúng bản xuất này.
- Lệnh train mẫu, ví dụ `yolo train model=yolo26s.pt data=data.yaml imgsz=1280`.
- Tùy chọn nén thành `.zip`.

**Nhập nhãn có sẵn:** đọc YOLO txt và COCO JSON. Nhờ vậy, dữ liệu đã gán trước đó bằng Roboflow, X-AnyLabeling hay CVAT đều đưa vào NTech Data Studio được.

## Thứ tự thực hiện và tiêu chí nghiệm thu

Làm theo 6 bước. Mỗi bước kết thúc bằng một bản chạy được, để dùng ngay cho dự án thật. Có 2 người thì làm bước 4–5 song song với bước 2–3.

> Sơ đồ lộ trình MVP (6 tuần, 2 cổng) là nội dung nhúng, không có trong bản xuất Markdown này.

Cổng ở bước 2 quyết định có cần hiển thị ảnh dạng pyramid hay không. Xong bước 3 là đã gán nhãn thật được, trong lúc các bước sau tiếp tục xây Module 3–4.

**Tiêu chí nghiệm thu MVP:**

- [ ] Import 1.000 ảnh BMP 20MP lẫn định dạng: audit và chuẩn hóa hết, không lỗi; dung lượng master giảm ít nhất 10 lần.
- [ ] Bộ test có gài sẵn ảnh trùng và ảnh gần trùng: tool phát hiện đúng toàn bộ.
- [ ] Zoom/pan mượt trên ảnh 20MP có 200 nhãn.
- [ ] Gán được box, polygon, OBB có undo/redo; tắt app đột ngột không mất nhãn.
- [ ] 300 ảnh gốc tạo version nhân ×3; vẽ overlay lên ảnh augment thấy nhãn khớp.
- [ ] Dataset YOLO26 detect/seg/obb train được ngay bằng `yolo train` mà không sửa tay; bản COCO train được với RF-DETR.
- [ ] Round-trip: nhập lại chính bản xuất YOLO/COCO thì nhãn giống hệt.

## Rủi ro, license và giai đoạn sau MVP

Rủi ro lớn nhất khi bán thương mại nằm ở **license của mô hình**, không nằm ở tool gán nhãn.

| Rủi ro | Ảnh hưởng | Cách xử lý |
| --- | --- | --- |
| Ultralytics YOLO (kể cả YOLO26) dùng AGPL-3.0; model train ra cũng thuộc AGPL ([Ultralytics](https://www.ultralytics.com/request-license)) | Trainer thương mại nhúng YOLO phải mua Enterprise License | MVP chỉ **xuất file** định dạng YOLO, không nhúng code Ultralytics. Khi làm Trainer: mặc định RF-DETR, YOLO là tuỳ chọn có license |
| RF-DETR: code và các cỡ Nano–Large, Seg dùng Apache 2.0; cỡ Atto/Femto/Pico/XL/2XL cần gói Roboflow ([Roboflow](https://roboflow.com/model-licenses/rf-detr)) | Chọn nhầm cỡ là vướng license | Trainer chỉ hiển thị các cỡ Apache 2.0 |
| Hiệu năng WPF khi ảnh rất lớn (trên 50MP) | Zoom/pan bị giật | Làm thử ngay ở bước 2 với ảnh 20MP và 50MP. Nếu chậm: hiển thị ảnh pyramid nhiều mức |
| Biến đổi nhãn khi augment sai (lệch vài pixel, lật ngược trục) | Model học sai mà không ai biết | Unit test cho từng phép, đối chiếu với ma trận chuẩn; luôn có preview |
| Phạm vi phình ra (SAM, video, multi-user) | Trễ MVP | Các tính năng này đã khóa vào giai đoạn sau |

Mình không phải luật sư. Trước khi bán, nên nhờ người chuyên về license rà lại toàn bộ dependency.

**Sau MVP, xếp theo thứ tự giá trị:**

1. **Pre-label bằng model:** chạy model ONNX qua ONNX Runtime (MIT) ngay trong C#. Người dùng chỉ cần sửa, tốc độ gán nhãn tăng nhiều lần.
2. **SAM click-to-segment:** encoder/decoder ONNX. Kiểm tra license từng phiên bản SAM trước khi nhúng.
3. **Trainer (đi cùng NTech Data Studio):** process Python riêng (PyTorch, RF-DETR) giao tiếp với app qua HTTP local. App gọi train, theo dõi metric, xuất ONNX/TensorRT, rồi nạp model ngược lại cho pre-label. Đây là vòng active learning. Đầu ra giao cho bên ngoài duy nhất là file `.nvmodel`, dành cho NVision. Thiết kế hiện hành của Trainer nằm ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md`, đổi thứ tự (làm YOLO detect nhiều phiên bản trước RF-DETR) và cách giao tiếp theo quyết định 0009 và 0010.
4. Keypoint/pose, video, nhiều người dùng qua LAN, đóng gói và license key.
