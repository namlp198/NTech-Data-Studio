# Màn hình Dataset (Module 3)

Màn hình dạng trang (như Prepare): tiêu đề, nội dung, một nút chính "Tạo version". Chia tập, preprocessing và tăng cường nằm trong một Version ([quyết định 0007](../../../decisions/0007_version-freezes-membership-and-config-not-pixels.md)). Không có gì được ghi cho đến khi bấm **Tạo version**; mọi chỉnh sửa chỉ tính lại bản xem trước.

## Bố cục

| Vùng | Nội dung |
| --- | --- |
| Đầu trang | Tiêu đề, ô tên version (tự gợi ý `v001`, `v002`...), nút Tạo version |
| Cột trái (thiết lập) | 1. Chia tập. 2. Preprocessing. 3. Tăng cường |
| Cột phải (kết quả) | Bốn ô số liệu (Train, Val, Test, Xuất ra), bảng ảnh/nhãn theo class, ghi chú và cảnh báo, preview 3×3, các version đã tạo |

Mỗi lần sửa thiết lập, sau 250 ms màn hình tính lại kế hoạch (trên luồng nền) và dựng lại preview. Sửa tiếp trong lúc đang tính thì lần tính cũ bị hủy.

## Chia tập

- Tỉ lệ train/val/test (số nguyên, tổng 100, train > 0, test được để 0) và seed (mặc định 70/20/10 và 42).
- **Cân bằng theo class**: mỗi ảnh thuộc nhóm của class hiếm nhất mà nó có; các nhóm được rải theo tỉ lệ, nhóm hiếm được rải trước. Sau đó một bước sửa chuyển một đơn vị nhỏ nhất để mỗi class có từ hai đơn vị trở lên có mặt ở cả train và val (xem `SplitService`).
- **Chia theo thư mục gốc**: ảnh có cùng `group_key` (thư mục con lúc import) luôn cùng một tập. Dưới 3 nhóm thì không chia theo nhóm được, app chuyển sang chia theo ảnh gốc và báo rõ. Ảnh cắt từ cùng một ảnh gốc (tile) luôn cùng một tập, bất kể tùy chọn này.
- **Gồm ảnh chưa gán nhãn (New)**: tắt theo mặc định, vì ảnh chưa gán đọc như "không có vật thể" khi train. Ảnh Null thì có mặt, làm ảnh nền.
- Chia theo nhóm với ít nhóm thì tỉ lệ thực tế lệch xa tỉ lệ mục tiêu (ví dụ 6 thư mục ra 52/34/14 thay vì 70/20/10). Màn hình cảnh báo khi một tập lệch quá 10 điểm phần trăm; tắt "chia theo thư mục" nếu các thư mục chỉ là cách sắp xếp chứ không phải các phiên chụp khác nhau.

## Preprocessing (mọi tập)

- Kích thước: giữ nguyên master (khuyến nghị cho YOLO26), Stretch, Fit (thêm viền xám 114, letterbox) hoặc Fill (phủ kín rồi cắt). Chiều rộng và cao đích từ 16 đến 16384.
- Chuyển ảnh xám.
- Gộp hoặc bỏ class: mỗi class chọn Giữ nguyên, Bỏ class này, hay Gộp vào một class khác. Đích gộp không được chính nó bị gộp hay bỏ. Class bị gộp mất dòng riêng trong bảng và nhãn của nó tính cho class đích.
- Bỏ ảnh không nhãn theo tỉ lệ: ảnh Null, hoặc ảnh bị bỏ trống nhãn vì bỏ class, được chọn bỏ theo thứ tự băm cố định từ seed.
- **Chưa có:** tile và crop ROI tĩnh trong Module 3. Tile đã có ở bước Chuẩn hóa của Prepare (tạo master), nên chưa cần lặp ở đây; xem `tasks/backlog.md`.

## Tăng cường (chỉ train)

Hệ số nhân 1 đến 10, tính cả ảnh gốc (ảnh gốc luôn được giữ, nên 300 ảnh chia 70/20/10 và ×3 ra 630 + 60 + 30). Mỗi dòng có công tắc và biên độ (đối xứng, "10" nghĩa là từ −10 đến +10). Ba nút mẫu: Tắt; Điều kiện nhà máy (ánh sáng, blur, nhiễu, nén JPEG, không lật, ×3, hợp với YOLO26 vốn đã tự tăng cường khi train); Hình học + ánh sáng (×3, có lật ngang, cho RF-DETR).

| Nhóm | Phép | Ảnh hưởng tới nhãn |
| --- | --- | --- |
| Hình học | Lật ngang, lật dọc, xoay 90° (cùng chiều, ngược chiều, 180°) | Biến đổi tọa độ chính xác |
| Hình học | Xoay nhỏ, scale, dịch, shear | Box lớn ra thành hộp bao quanh hộp đã xoay; polygon cắt tại biên ảnh; OBB fit lại bằng hình chữ nhật diện tích nhỏ nhất; nhãn còn dưới 30% diện tích trong ảnh bị bỏ |
| Ánh sáng, màu | Độ sáng, phơi sáng, tương phản, gamma, hue, bão hòa, ảnh xám ngẫu nhiên | Không |
| Nhiễu | Gaussian blur, motion blur, nhiễu, nén JPEG gây vỡ | Không |
| Che khuất | Cutout (số ô, kích thước), tùy chọn bỏ nhãn bị che từ X% | Chỉ khi bật bỏ nhãn |

Xoay 90° chọn ngẫu nhiên trong các phép đang bật và cả "không xoay", nên một bản sao không bị ép phải xoay. Lật mỗi chiều có xác suất 50% khi bật.

Mọi phép hình học, cùng preprocessing Fit/Stretch/Fill, được gộp thành một ma trận affine (`RenderPlan.Transform`): ảnh được warp một lần, nhãn đi qua cùng ma trận (`LabelTransformer`). Ảnh và nhãn dùng chung một nguồn nên không thể lệch nhau.

**Giới hạn đã biết:**

- OBB sau khi bị biên ảnh cắt ngang được fit lại và có thể nhô vài pixel ra ngoài ảnh. Bước ghi nhãn (Module 4) phải xử lý: kẹp từng góc sẽ làm méo hình chữ nhật, nên chọn cách rút nhỏ hay chấp nhận méo nhẹ khi viết exporter.
- Polygon lõm bị cắt bằng Sutherland–Hodgman có thể có cạnh chồng lên biên; diện tích vẫn đúng.
- Vùng trống do xoay, scale nhỏ hay dịch được tô xám 114; ô cutout tô xám 128.

## Preview 3×3

Mỗi ô là ảnh thật sẽ được xuất ra, dựng bởi cùng engine với bản xuất, rồi vẽ nhãn sau biến đổi (`LabelPreview`). Chỉ lấy từ tập train. Ảnh lớn hơn 640 px được dựng nhỏ lại cho nhanh, nhãn theo cùng ma trận nên vẫn khớp. "Xáo lại" chọn ảnh khác và bản sao khác. Chưa bật tăng cường thì các ô hiện ảnh gốc sau preprocessing (nút hiển thị ghi chú nói rõ điều này).

## Trạng thái màn hình

| Trạng thái | Hiển thị |
| --- | --- |
| Chưa mở project | Thông báo, không có form |
| Đang tải | "Đang tải ảnh và nhãn của project..." |
| Lỗi tải | Thông báo lỗi và nút Thử lại |
| Chưa có ảnh | Hướng dẫn nhập ảnh và gán nhãn |
| Chưa có ảnh đủ điều kiện, hoặc config sai | Bảng kết quả bị thay bằng một dòng giải thích; nút Tạo version bị khóa; lỗi config hiện bằng chữ đỏ ngay dưới tiêu đề |
| Đang dựng preview, lỗi dựng | Dòng "Đang dựng preview..."; ô lỗi hiện lý do thay cho ảnh, các ô khác vẫn hiện |
| Không có version | "Chưa có version nào." |

Xóa version hỏi xác nhận trước (ảnh, nhãn và version khác không bị ảnh hưởng).

## Đã kiểm tra

- Test: tọa độ affine, clip, hình chữ nhật nhỏ nhất, biến đổi nhãn qua từng phép, sampler (khoảng giá trị, tái lập, độc lập giữa các tùy chọn), planner, chia tập (tỉ lệ, thứ tự đầu vào, nhóm, stratify, sửa class hiếm, có đột biến kiểm chứng), VersionBuilder, kho SQLite, preview. Engine OpenCV: với mỗi phép (12 biến thể + 3 chế độ resize + ảnh 3200 px bị thu nhỏ), tâm khối trắng trong ảnh xuất trùng tâm nhãn đã biến đổi trong sai số 1 px.
- Chạy app thật qua UI Automation trên 48 ảnh tổng hợp có nhãn: các trạng thái chưa project và chưa ảnh, số liệu và cảnh báo, báo lỗi tỉ lệ sai, ba mẫu tăng cường, preprocessing Fit và gộp class, tạo, từ chối tên trùng, xóa có xác nhận. Kill tiến trình rồi đọc nguội DB: `integrity_check` ok, số ảnh mỗi tập khớp màn hình, không nhóm nào nằm ở hai tập, mọi class có mặt ở cả train và val.
- Xem bằng mắt qua ảnh chụp: nhãn bám đúng vật thể trong các ô xoay, dịch, scale, letterbox và gộp class.
