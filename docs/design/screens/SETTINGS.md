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

## Tùy chọn nâng cao: gán nhãn tự động (quyết định 0017 và 0018)

- Thẻ riêng ở Cài đặt, nằm sau thẻ model Magic polygon. Ô "Bật gán nhãn tự động" (AutomationId `AutoLabelEnabled`) mặc định **tắt**; tắt thì Annotate không có nút Gán tự động và phần còn lại của thẻ ẩn.
- Bật thì hiện: ô chọn "Cách gán" (`AutoLabelMode`: Model segment, hoặc Model detect + SAM), đoạn giải thích cách đang chọn (`AutoLabelModeHelp`), đường dẫn và trạng thái model của cách đó (`AutoLabelModelPath`, `AutoLabelStatus`), nút Chọn model / Bỏ model, thanh ngưỡng tin cậy dùng chung.
- Mỗi cách có đường dẫn model riêng (segment và detect), đổi cách không làm mất cái kia; tắt công tắc cũng giữ cả hai. Trạng thái "model dùng được" của cách detect + SAM đòi cả model detect lẫn model SAM.
- Trạng thái: loading không có (đọc model khi gán lần đầu), empty là "Chưa chọn model ...", error là lý do model bị từ chối (thiếu `model.meta.json`, sai loại, không nhúng NMS, thiếu model SAM).

## Bố cục chia mục (quyết định 0019)

Cài đặt có danh sách mục bên trái (Gán nhãn tự động, Magic polygon (SAM), Phím tắt) và nội dung của mục đang chọn bên phải; chọn mục qua `IsChecked` của RadioButton (`SettingsSectionAutoLabel`, `SettingsSectionMagic`, `SettingsSectionShortcuts`). Mục Gán nhãn tự động: hàng công tắc nâng cao, rồi hai thẻ riêng cho Detect (box) và Segment (polygon). Mỗi thẻ có huy hiệu trạng thái (Sẵn sàng, Chưa có model, Có lỗi), các hàng Model, Trạng thái, Ngưỡng tin cậy; thẻ Segment thêm hàng Cách gán. Model detect ở thẻ Detect cũng là model của cách detect + SAM. Mục Magic polygon có huy hiệu trạng thái của model SAM.
