# Màn hình Cài đặt

Tab cuối trên thanh trên cùng. Cài đặt của riêng người dùng trên máy (quyết định 0015), lưu ở `%APPDATA%\NTech\DataStudio\settings.json`, không nằm trong project. Cũng giữ project mở gần nhất (`LastProject`) để lần sau tự mở; Đóng project thì xóa.

## Phím tắt (Hot-Key)

Danh sách mọi hành động của Annotate, chia ba nhóm (Công cụ, Xem và duyệt ảnh, Chỉnh sửa nhãn). Mỗi dòng: tên hành động, phím hiện tại, nút **Đổi phím** và nút **Mặc định** (chỉ bật khi dòng đã bị đổi). Nút **Đặt lại tất cả** ở đầu mục.

- **Đổi phím:** bấm nút (chữ nút thành "Nhấn phím..."), rồi nhấn phím hoặc tổ hợp Ctrl/Shift. Phím được gán và lưu ngay; Annotate dùng được ngay, tooltip trên thanh công cụ đổi theo. Esc hủy. Bấm lại nút của dòng đang chờ cũng hủy. Mọi phím (kể cả Tab, Enter, Space) đi vào dòng đang chờ, không bấm nhầm nút khác.
- **Bị từ chối, kèm lý do ngay trên màn hình:** phím đang dùng cho việc khác (nói tên việc đó), phím số 1 đến 9 (chọn class), tổ hợp có Alt. Space gán được; nó chạy khi nhả phím và không chạy nếu trong lúc giữ bạn đã kéo pan. Phím Ctrl hay Shift đứng một mình được bỏ qua và vẫn chờ.
- **Trạng thái:** một dòng báo kết quả lần đổi gần nhất; lỗi hiện màu lỗi. Không đọc được hay không ghi được file thì có dòng cảnh báo trên đầu trang (xem quyết định 0015, mục 8). Không có trạng thái trống hay đang tải: danh sách luôn đầy đủ và đọc ngay lúc khởi động.
- Có ghi chú về bộ gõ Telex (CLAUDE.md, "Bẫy đã biết").

## Chưa làm

Nhiều phím cho một hành động từ giao diện, nhập/xuất cấu hình, phím tắt chuyển tab, mục cài đặt khác ngoài phím tắt.

## Model Magic polygon (SAM)

Mục đầu trang (quyết định 0016). Hiện trạng thái (đã có đủ hai file, hay chưa có), đường dẫn thư mục model, và:

- **Tải model (44,7 MB):** chỉ hiện khi thiếu model. Có thanh tiến độ và dòng "Đang tải <file> (1/2): x MB / y MB", nút **Hủy**. Xong thì nút biến mất và công cụ ở Annotate bật ngay. Lỗi (mất mạng, mã lỗi máy chủ, sai SHA-256) hiện màu lỗi; bấm tải lại thì bắt đầu sạch. File dở không để lại.
- **Mở thư mục model:** mở thư mục trong Explorer, để chép tay hai file vào máy không có mạng.
- Không có trạng thái "trống": mục luôn hiện đúng một trong ba trạng thái (thiếu, đang tải, đã có).
