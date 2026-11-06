# Kiến trúc

Ứng dụng WPF trên .NET 10, chạy offline hoàn toàn.

## Các project

| Project | Target | Vai trò | Phụ thuộc |
| --- | --- | --- | --- |
| `NTech.DataStudio.Domain` | net10.0 | Hình học, nhãn, ảnh, class, version, profile. Không dùng thư viện ngoài. | không |
| `NTech.DataStudio.Application` | net10.0 | Use case và abstraction | Domain |
| `NTech.DataStudio.Imaging` | net10.0 | Đọc ảnh, chất lượng, biến đổi, tăng cường (OpenCvSharp4) | Application |
| `NTech.DataStudio.Storage` | net10.0 | SQLite, thư mục project, đọc YAML profile | Application |
| `NTech.DataStudio.Exporters` | net10.0 | YOLO26, COCO | Application |
| `NTech.DataStudio.Desktop` | net10.0-windows | WPF, ViewModel, composition root | tất cả |

Hướng phụ thuộc: `Desktop → Application → Domain`. Imaging, Storage và Exporters hiện thực các abstraction của Application và chỉ được nối với nhau ở `Bootstrap/AppComposition.cs`.

## Ranh giới sản phẩm

- NTech Data Studio gồm công cụ dữ liệu (MVP) và Trainer (sau MVP, `NTech.DataStudio.Trainer`).
- NVision là sản phẩm riêng, chỉ nhận file `.nvmodel` do Trainer xuất.

## Lệch so với cây thư mục trong tài liệu thiết kế

- `YamlModelProfileCatalog` đặt trong Storage: thiết kế nêu YamlDotNet nhưng chưa chỉ định project.
- Test của Storage nằm trong `NTech.DataStudio.Application.Tests`, vì thiết kế không có project test riêng cho Storage.
- `IImageCodec` gộp toàn bộ thao tác trên điểm ảnh của Prepare (đọc header, phân tích, chuẩn hóa, preview). `IPrepareStore` thêm vào danh sách abstraction của thiết kế.
- `IAnnotationStore` là abstraction thứ ba của Application cho lưu trữ, cạnh `IPrepareStore`: một connection sống đúng lúc màn hình Annotate hiển thị.
- `AnnotationEditor` nằm ở Application chứ không phải Desktop: logic chuột và phím làm việc theo pixel ảnh nên test được không cần cửa sổ. `AnnotationCanvas` chỉ đổi chuột sang pixel ảnh và vẽ.
- `Themes/Components/InputControls.xaml` không được copy: file đó chỉ tạo kiểu cho `RoundActionButton` của robot và dùng gradient, trái quy tắc của repo này.
