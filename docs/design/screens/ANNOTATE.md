# Màn hình Annotate

Canvas ảnh full-bleed, mọi thứ khác nổi lên trên. Mỗi thao tác được lưu vào `project.db` ngay (không có nút Save): kill app giữa chừng không mất nhãn đã hiện trên màn hình.

## Bố cục

- **Tool rail (trái):** Chọn (V), Box (B), Polygon (P), Rotated box (R), Tag (T); Undo, Redo, Xóa, Ẩn/hiện nhãn; Vừa khung, Phóng to, Thu nhỏ.
- **Panel phải:** danh sách class (phím tắt, màu, số nhãn), ô tên class với Thêm, Đổi tên, Xóa, bảng 10 màu, Gộp vào; danh sách nhãn của ảnh (Ẩn, Khóa, bấm để zoom tới nhãn); thanh độ trong suốt.
- **Dải đáy:** lọc theo trạng thái và class, nút Chép nhãn ảnh trước, Null, Duyệt, và các thumbnail có chấm trạng thái và số nhãn.
- **Strip Kết thúc / Bỏ đỉnh / Hủy** hiện giữa màn hình khi đang vẽ.

## Vẽ và sửa

| Công cụ | Cách dùng |
| --- | --- |
| Box | Kéo hai góc. Có đường gióng toàn ảnh. Bấm không kéo thì không tạo gì |
| Polygon | Bấm từng đỉnh. Enter hoặc bấm lại đỉnh đầu để đóng; Backspace bỏ đỉnh vừa vẽ (đỉnh cuối cùng thì hủy cả hình); Esc hủy. Hình tự cắt không kết thúc được và có thông báo |
| Rotated box | Kéo trục chính, rồi bấm để chốt bề rộng. Bấm-bấm-bấm cũng được |
| Chọn | Bấm để chọn hình trên cùng. Kéo hình để di chuyển. Kéo ô vuông ở đỉnh để sửa. Bấm đúp lên cạnh polygon để thêm đỉnh. Delete để xóa |
| Tag | Gắn nhãn phân loại cho cả ảnh: tick ô cạnh class, hoặc bấm phím số |

- Mọi điểm bị kẹp trong ảnh. Kéo đỉnh polygon vào chỗ làm hình tự cắt thì đỉnh dừng ở vị trí hợp lệ gần nhất. Kéo góc box giữ cạnh song song trục; kéo góc rotated box giữ nguyên góc xoay và chân vào góc đối diện, nếu vượt biên thì hộp co lại chứ không méo.
- Hình bị khóa chọn được nhưng không di chuyển, sửa hay xóa được. Hình ẩn không chọn được.
- Hình nhỏ vẫn chọn được nhờ chạm vào viền, không chỉ bên trong.

## Phím tắt

| Phím | Chức năng |
| --- | --- |
| V (hoặc S), B, P, R, T | Chọn, Box, Polygon, Rotated box, Tag |
| Q, Shift+Q | Công cụ kế, công cụ trước trong thanh công cụ (Chọn, Box, Polygon, Rotated box, Tag, rồi quay vòng); đang vẽ dở thì bỏ hình dở |
| + và - | Phóng to, thu nhỏ quanh tâm khung |
| 1–9 | Chọn class. Công cụ Tag: gắn hoặc bỏ tag. Công cụ Chọn đang chọn một hình: đổi class của hình |
| A, D hoặc ←, → | Ảnh trước, ảnh sau (theo bộ lọc đang bật) |
| Enter, Backspace, Esc | Kết thúc, bỏ đỉnh vừa vẽ, hủy (không vẽ thì bỏ chọn) |
| Ctrl+Z, Ctrl+Y | Undo, Redo (riêng cho từng ảnh) |
| Ctrl+C, Ctrl+V | Sao chép và dán một hình. Dán trên cùng ảnh thì lệch 12 px mỗi lần; sang ảnh khác cỡ thì co theo tỉ lệ |
| Delete | Xóa hình đang chọn |
| N | Đánh dấu Null (chỉ khi ảnh chưa có nhãn) |
| H | Ẩn/hiện mọi nhãn |
| F | Vừa khung |
| Space + kéo, chuột giữa kéo | Pan |
| Kéo chuột trái trên chỗ trống khi đã zoom lớn hơn vừa khung | Pan. Chỉ với công cụ Chọn (bấm vào nhãn thì vẫn chọn và kéo nhãn) và công cụ Tag; công cụ vẽ cần cú kéo để vẽ nên dùng Space hoặc chuột giữa. Bấm không kéo vẫn bỏ chọn |
| Cuộn chuột | Zoom quanh con trỏ |

Phím tắt không chạy khi đang gõ trong ô tên class hay mở combobox.

## Trạng thái ảnh

`New → Labeled → Reviewed`, và `Null` cho ảnh không có vật thể.

- Thêm nhãn hay tag: thành Labeled. Xóa hết nhãn: về New (Null giữ nguyên Null).
- Sửa ảnh đã Reviewed: về Labeled, cần duyệt lại.
- Null chỉ đặt được khi ảnh không có nhãn. Bấm lại để bỏ.
- Duyệt chỉ áp dụng cho ảnh Labeled hoặc Null; bấm lại để bỏ duyệt.
- Bộ lọc "Cần review" là các ảnh Labeled chưa duyệt.

## Chép nhãn từ ảnh trước

Chép cả nhãn và tag của ảnh đứng trước trong toàn project (không theo bộ lọc), co theo tỉ lệ nếu hai ảnh khác cỡ. Là một thao tác undo được.

## Quản lý class

- Class mới nhận màu kế tiếp trong 10 màu mặc định (11 trở đi xoay vòng) và phím số thấp nhất còn trống từ 1 đến 9. Class thứ 10 trở đi không có phím tắt.
- Tên không được trống, dài quá 64 ký tự hay trùng (không phân biệt hoa thường).
- **Gộp** chuyển mọi nhãn và tag sang class đích. **Xóa** xóa luôn nhãn và tag của class, ảnh hết nhãn quay về New. Cả hai hỏi xác nhận khi class đang có nhãn, và không hoàn tác được; lịch sử undo và clipboard bị xóa.

## Khác với tài liệu thiết kế

- Không có "trả lại" khi review, chỉ có "Bỏ duyệt" (về Labeled).
- Rotated box chưa có tay cầm xoay; chỉ di chuyển và kéo góc.
- Chưa có ô kéo ở giữa cạnh của box.
- Lịch sử undo chỉ sống trong lúc màn hình Annotate đang mở; rời sang màn hình khác là mất.
- Ctrl+C chỉ sao chép một hình.
- Tải trước chỉ làm cho ảnh kế tiếp theo chiều tiến.
- Sau khi hoàn tác một lần xóa, hình quay lại ở cuối danh sách nên nằm trên cùng khi chồng lấp.

## Hiệu năng (cổng bước 2 của lộ trình)

Đo bằng chính `AnnotationCanvas` của app, 200 nhãn lẫn box, polygon, rotated box, quét zoom từ toàn ảnh xuống 5% trong lúc dịch tâm, 5 giây mỗi lần. Máy 8 luồng, GPU render tier 2:

| Ảnh | Giải mã đầy đủ | Bộ nhớ ảnh | Có 200 nhãn | Chỉ ảnh |
| --- | --- | --- | --- | --- |
| 20 MP (5472 × 3648) | 144 ms | 76 MB | 55 fps, 8 khung trên 33 ms, tối đa 50 ms | 61 fps, 0 khung trên 33 ms |
| 50 MP (8192 × 6144) | 269 ms | 192 MB | 59 fps, 5 khung trên 33 ms, tối đa 56 ms | 60 fps, 0 khung trên 33 ms |

Kết luận: chưa cần ảnh pyramid ở mức này. Giới hạn của phép đo: ảnh tổng hợp nén rất nhỏ (1 MB và 4 MB), nên thời gian giải mã ảnh chụp thật có nhiễu sẽ lâu hơn; chỉ đo trên GPU phần cứng; cạnh ảnh trên 16.384 px vượt giới hạn texture của GPU và chưa thử.

### Bộ gõ tiếng Việt

Phím tắt là chữ đơn, nên bộ gõ tiếng Việt kiểu Telex (Unikey, EVKey) làm hỏng chúng: gõ `e` hai lần thành `ê` nghĩa là app nhận Backspace thay vì `e`, và `a`, `d`, `w`, `s`, `r` cũng bị biến đổi khi đứng sau một nguyên âm. Tắt tiếng Việt (chuyển về gõ tiếng Anh) khi gán nhãn. App đã tắt IME của Windows trên canvas, nhưng bộ gõ chạy ở mức hệ điều hành thì app không chặn được; vì vậy Q (không thuộc phím nào của Telex) được chọn cho việc đổi công cụ, không dùng E. Đã gặp lỗi này khi chạy app trên máy có Telex bật.
