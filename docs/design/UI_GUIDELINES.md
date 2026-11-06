# UI guidelines

Rút gọn từ UI guidelines của robo-station, bỏ phần robot và mission.

## Phong cách

Modern industrial control: tối, rõ ràng, desktop-first, ưu tiên quan sát dữ liệu.

## Bố cục

- **TopBar 48 px:** trái là ViewSelector (Project, Prepare, Annotate, Dataset, Export); phải là StatusIconStrip (tên project, số ảnh đã gán / tổng số, cảnh báo validator). Không có page title lớn hay breadcrumb trên TopBar.
- **Annotate:** canvas ảnh full-bleed. Tool rail 56 px nổi bên trái, floating panel 320 px bên phải (Class và Nhãn), dải thumbnail nổi ở đáy, strip Finish/Undo/Cancel.
- **Project, Prepare, Dataset, Export:** layout trang gồm PageHeader, nội dung, primary action.

## Quy tắc bắt buộc

- View không hard-code màu hay khoảng cách; chỉ dùng `StaticResource` từ `Themes/`.
- Logic nằm trong ViewModel, dùng binding và `ICommand`. Code-behind chỉ để xử lý chuột và vẽ trên canvas.
- Mỗi màn hình có đủ trạng thái loading, empty, error.
- Không dùng emoji thay icon, không gradient, không shadow đậm, không bo góc lớn.
- Màu chỉ dùng khi có ý nghĩa: Brand cho hành động, Status cho trạng thái, màu class chỉ dùng cho nhãn.
- Giá trị số và tọa độ dùng font monospace.
- Thông báo lỗi nói rõ chuyện gì xảy ra và người dùng làm gì tiếp, bằng tiếng Việt.
