# Màn hình Project

Chọn, tạo, mở project; chọn task mục tiêu; đóng gói và mở từ gói .zip. Mỗi project là một thư mục tự chứa (`project.db`, `source`, `master`, `thumbs`, `rejected`, `versions`, `exports`).

## Task mục tiêu (quyết định 0014 và 0019)

Một dãy ô tích Detect, Segment, OBB, Classify, tích được nhiều; không tích gì là "chưa chọn" (dùng được mọi công cụ vẽ). Tích hoặc bỏ tích được lưu ngay. Khi tạo project, Segment được tích sẵn (polygon xuất được cả Segment lẫn Detect). Đổi task không xóa hay sửa nhãn.

## Đóng gói project (quyết định 0020)

- Thẻ "Đóng gói project" chỉ hiện khi project đang mở. Hai ô tích: "Kèm ảnh nguồn" (kèm dung lượng, mặc định bật) và "Kèm dataset đã xuất" (mặc định tắt); dòng bên dưới nói dung lượng từng phần và gói dự kiến.
- Nút "Đóng gói..." hỏi thư mục lưu rồi tạo `<tên>_v001.zip` (số tăng dần); có thanh tiến độ, nút Hủy, nút "Mở thư mục chứa file" sau khi xong. Đóng gói được cả khi project đang mở.
- "Mở từ file .zip..." có ở cả trạng thái có project (cạnh Mở project khác) và chưa có project (thẻ riêng): chọn gói, xem nội dung trong hộp xác nhận, chọn thư mục đặt, rồi giải nén và mở.
- Trạng thái: loading (đang tính dung lượng, thanh tiến độ khi chạy), empty (chưa có project: chỉ có thẻ mở), error (dòng đỏ `PackageError`: gói hỏng, gói của bản app mới hơn, ổ đĩa đầy, thư mục đã có dữ liệu).
