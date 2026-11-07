# Màn hình Export (Module 4)

Màn hình dạng trang (như Dataset): tiêu đề, nội dung, một nút chính "Xuất dataset". Chọn một version và một model profile; app kiểm tra ngay (không ghi gì), có lỗi thì khóa nút, chỉ có cảnh báo thì vẫn xuất được. Các lựa chọn khi ghi file nằm ở [quyết định 0008](../../../decisions/0008_export-format-choices.md).

## Bố cục

| Vùng | Nội dung |
| --- | --- |
| Đầu trang | Tiêu đề, nút Xuất dataset |
| Dưới tiêu đề | Thanh tiến độ và nút Hủy (khi đang xuất); kết quả và nút Mở thư mục; lỗi |
| Cột trái (Bản xuất) | Version, model profile, imgsz, định dạng ảnh đã chỉnh, thư mục sẽ ghi (Chọn thư mục / Mặc định), nén zip, ghi đè |
| Cột phải | Kiểm tra trước khi xuất: tóm tắt "N lỗi, M cảnh báo" và danh sách (đỏ là lỗi, vàng là cảnh báo) |

Đổi version, profile hay imgsz thì kiểm tra lại (trên luồng nền; lần cũ bị hủy). Chọn profile thì imgsz nhảy về mặc định của profile.

## Profile và định dạng

Danh sách profile lấy từ `profiles/*.yaml`, lọc những cái có bộ xuất (anomaly không có).

| Profile | Định dạng | Cấu trúc |
| --- | --- | --- |
| yolo26-detect | YOLO txt | `images/{train,val,test}`, `labels/{…}`, `data.yaml`; `cls cx cy w h` |
| yolo26-segment | YOLO txt | như trên; `cls x1 y1 x2 y2 …` |
| yolo26-obb | YOLO txt | như trên; `cls x1 y1 … x4 y4` |
| yolo26-classify | thư mục | `train/<class>/*.jpg`, `val/<class>/…`; không có file nhãn |
| rf-detr-detect, rf-detr-segment | COCO json | `train/`, `valid/`, `test/`, mỗi thư mục có `_annotations.coco.json` |

Mọi bản xuất có thêm `version.json` (config đúng như lưu, seed, danh sách class kèm số nhãn) và `README.dataset.md` (số lượng, preprocessing, tăng cường, cảnh báo, lệnh train mẫu). Thư mục đích mặc định là `<project>/exports/<version>_<định dạng>`; có thể chọn thư mục khác, và nén thêm thành `.zip` cạnh nó.

## Kiểm tra trước khi xuất

**Lỗi (chặn xuất):** version không có ảnh hoặc không có ảnh train; master không còn trên đĩa; nhãn thuộc class không tồn tại; nhãn có diện tích 0; nhãn nằm ngoài ảnh (quá 0,5 px); polygon tự cắt; profile Segment mà ảnh chỉ có box; phân loại mà ảnh không có đúng một tag; imgsz không hợp lệ; profile không có định dạng xuất.

**Cảnh báo:** class có dưới 50 nhãn (50 ảnh với phân loại); val thiếu class có trong train; không có ảnh val; ảnh chưa gán (New) nằm trong version; nhãn point bị bỏ; polygon thành box (detect) hoặc thành hình chữ nhật (OBB); box thành polygon (segment); nhãn nhỏ hơn `min_object_px` ở imgsz đã chọn (tính cả resize của preprocessing); imgsz không chia hết cho stride.

Bảng gộp class của version được áp trước khi kiểm tra: nhãn của class bị bỏ không được xét, tag đi qua bảng gộp.

## Trạng thái màn hình

| Trạng thái | Hiển thị |
| --- | --- |
| Chưa mở project | Thông báo, không có form |
| Đang tải | "Đang tải các version của project..." |
| Lỗi tải | Thông báo lỗi và nút Thử lại |
| Chưa có version | Hướng dẫn tạo version ở Dataset |
| Đang kiểm tra | "Đang kiểm tra..."; nút Xuất khóa |
| Đang xuất | Tiến độ "Đang dựng ảnh n/N", nút Hủy; form khóa |
| Có lỗi | Danh sách đỏ; nút Xuất khóa |
| Thư mục đích đã có | Lỗi nêu đường dẫn; bật "Ghi đè" để thay (chỉ thay bản xuất cũ của app) |

## Đã kiểm tra

- Test: shape nhãn (kể cả OBB nhô ra ngoài), validator (từng lỗi và cảnh báo, gộp class, phân loại), từng exporter (dòng nhãn mẫu, file rỗng cho ảnh nền, yaml an toàn với tên có dấu hai chấm và dấu thăng), vòng đọc lại YOLO và COCO ra đúng nhãn, và `ExportService` trên project thật với ảnh master thật và engine OpenCV: cấu trúc thư mục, ảnh gốc chép nguyên byte, nhân ×3 chỉ ở train, nhãn bám khối trắng sau lật, xoay, scale, dịch (sai số tâm dưới 3 px), cùng một version xuất hai lần ra cùng byte, bản dở bị dọn khi hủy hoặc khi một master hỏng, không ghi đè thư mục lạ, zip, COCO, phân loại.
- Đột biến kiểm chứng: cho `ExportService` tính nhãn bằng một `RenderPlan` không có tăng cường thì test căn nhãn thất bại; khôi phục thì qua.
- Chạy app thật qua UI Automation (48 ảnh tổng hợp, 44 có nhãn): các trạng thái chưa project và chưa version; xuất YOLO detect, lần xuất thứ hai bị từ chối và bật ghi đè thì qua; Segment bị chặn vì chỉ có box; phân loại bị chặn vì chưa có tag; COCO có zip; imgsz "abc" bị báo lỗi. Đọc file trên đĩa: số ảnh, số file nhãn, số dòng nhãn khớp màn hình và README, mọi tọa độ trong 0 đến 1. Vẽ nhãn từ file `.txt` lên ảnh đã xuất: khung bám đúng khối màu, kể cả bản xoay và dịch.
- Lỗi tìm được khi chạy app: lần vào màn hình đầu tiên không kiểm tra gì (kiểm tra bị bỏ qua vì còn đang tải), nên nút Xuất bị khóa cho tới khi đổi một lựa chọn. Đã sửa.

## Chưa làm

- Nhập nhãn có sẵn (YOLO txt, COCO json) vào project: đã có bộ đọc (`YoloLabelParser`, `CocoLabelParser`) và test vòng đọc lại, nhưng chưa có màn hình hay service ghép nhãn vào ảnh của project. Xem `tasks/backlog.md`.
- Chưa chạy `yolo train` hay train RF-DETR bằng bản xuất.
