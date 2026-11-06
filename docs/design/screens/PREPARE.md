# Màn hình Prepare

Wizard 4 bước, chọn tự do trên dải bước. Tiến độ, nút Hủy, lỗi và kết quả nằm chung ở đáy. Chưa mở project thì chỉ có thông báo mời mở project.

## 1. Import

- Kéo thả thư mục hoặc chọn thư mục. Quét đệ quy: BMP, TIFF, PNG, JPEG, WebP.
- Chọn chép ảnh vào project hay chỉ tham chiếu.
- Chạy lại chỉ thêm file mới. Bước này chỉ đọc header; thư mục con trở thành `group_key`.

## 2. Audit

Số file, file hỏng, dung lượng, file lớn nhất, ước tính dung lượng master; phân bố độ phân giải, định dạng, độ sâu bit, số kênh. Ước tính dùng hệ số cố định và tính trước khi thu nhỏ; con số thật theo từng cấu hình nằm ở preview bước 4.

## 3. Làm sạch

| Luật | Mặc định | Ghi chú |
| --- | --- | --- |
| Trùng hoàn toàn | bật | SHA-256; giữ ảnh nhập trước |
| Gần trùng | bật, khoảng cách ≤ 5 | Chỉ cần pHash hoặc dHash đạt ngưỡng; giữ ảnh nét nhất |
| Mờ | **tắt** | Phương sai Laplacian trên ảnh thu về cạnh dài 2048. Ngưỡng gợi ý là phân vị thứ 5 |
| Quá tối | bật, ≥ 90% điểm ảnh dưới 30 | |
| Cháy sáng | bật, ≥ 50% điểm ảnh từ 250 | |

- Phân tích giải mã mỗi ảnh một lần và lưu ngay; hủy rồi chạy lại sẽ làm tiếp.
- "Áp dụng lại ngưỡng" chỉ tính lại, không đọc lại ảnh.
- Danh sách ảnh bị loại có thumbnail, lý do, chi tiết và nút Khôi phục. File hỏng không khôi phục được.
- Camera tĩnh cho nhiều ảnh gần giống nhau, nên có thể bị loại nhiều ở luật gần trùng. Khi đó hãy giảm ngưỡng hoặc tắt luật này.

## 4. Chuẩn hóa

- Chọn model profile và nhập kích thước vật thể nhỏ nhất. App đề xuất cách xử lý, `imgsz` và cảnh báo; người dùng vẫn đổi được.
- Downscale: cạnh dài tối đa 2048 hoặc 4096, không phóng to. Tile: kích thước và độ chồng lấn. Crop ROI: kéo chuột trên ảnh "Trước"; ảnh nhỏ hơn thì lấy phần giao, ảnh không giao với ROI thì báo lỗi.
- JPEG q95 hoặc PNG; ảnh mono giữ 1 kênh hoặc nhân thành 3 kênh.
- Preview trước/sau cho một ảnh, chuyển được giữa các ảnh, kèm dung lượng thật và ước tính cả bộ.
- Chạy lại với cùng cấu hình chỉ làm ảnh còn thiếu. Đổi cấu hình thì hỏi rồi tạo lại toàn bộ master. Đã có nhãn thì không cho đổi cấu hình.

## Giới hạn hiện tại

- Chưa áp dụng ICC profile nhúng trong ảnh: mọi ảnh được coi là sRGB.
- Audit hiển thị kích thước trong header, chưa tính xoay EXIF.
