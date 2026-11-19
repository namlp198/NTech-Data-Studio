# 0022: Mỗi ảnh một loại hình (box hoặc polygon), chuyển box thành polygon bằng SAM; xóa ảnh trong Annotate

Ngày: 2026-11-19

Quyết định 0019 cho box và polygon sống chung trên một ảnh trong project Detect + Segment. Dùng thật cho thấy luồng "gán box, cho model detect gán nốt, rồi dùng SAM làm bản Segment" để lại cả hai loại hình trên một ảnh, và Export xuất thành hai nhãn cho một vật.

**Quyết định:**

1. **Một ảnh chỉ có box hoặc polygon.** `ShapeFamilyRule`, gọi trong `AnnotateService.Execute` (chốt chặn duy nhất của mọi sửa nhãn, quyết định 0006): sửa nào **đưa vào** loại hình khác với loại đang có thì bị từ chối bằng `ShapeMixException`, kèm lời nói cách làm tiếp. Ảnh đã lẫn từ trước vẫn sửa, xóa, di chuyển được. Undo và Redo không đi qua quy tắc (chúng chỉ trả lại trạng thái đã hợp lệ). Áp cho vẽ tay, Magic polygon, dán, chép từ ảnh trước, gán tự động một ảnh và hàng loạt. RotatedBox và Point không nằm trong quy tắc.
2. **Chuyển box thành polygon bằng SAM** (`BoxToPolygonConverter`): mỗi box là gợi ý khung cho SAM, thay thế tại chỗ bằng polygon cùng class (nguồn `model`, chờ duyệt), trong **một lần Undo** cho mỗi ảnh. Box SAM không cắt được (hoặc kết quả lệch box, `BoxCutter.MinAgreement`) thành polygon hình chữ nhật chứ không giữ làm box, để ảnh không còn box nào; số lượng được báo để sửa tay. Có nút cho một ảnh và cho cả project. Dùng chung đoạn cắt với detect + SAM (0018) qua `BoxCutter`.
3. **Dataset Detect vẫn có từ polygon**: Export đã chuyển polygon thành box bao quanh (0008). Box lấy từ polygon hơi chặt hơn box vẽ tay ban đầu; đó là cái giá của việc không giữ hai bộ nhãn.
4. **Nhãn trộn trên cùng ảnh ở Export** vẫn chỉ là cảnh báo (`DatasetValidator`, nhãn gần trùng cùng class), như bạn chọn.
5. **Xóa ảnh trong Annotate** (cùng đợt): đánh dấu `images.status = 'rejected'` và nhớ trạng thái cũ ở cột mới `deleted_from` (migration 3); file và nhãn giữ nguyên (0005), `active_images` loại ảnh khỏi danh sách và dataset mới, Export bỏ ảnh đã xóa khỏi version cũ. Xóa một hoặc nhiều ảnh (Ctrl hoặc Shift chọn trên dải ảnh), Hoàn tác xóa, Khôi phục ảnh đã xóa. Không phải sửa nhãn nên không đi qua undo của nhãn.

**Phương án đã bỏ:** lưu hai tập nhãn song song theo task trên cùng ảnh (đúng hơn nhưng phải sửa bảng `annotations`, undo, lưu và Export).

**Hệ quả:** test đổi theo hai chỗ của 0019 (gán tự động thay thế theo loại hình giờ chỉ còn trong một loại hình); các test cũ cho box và polygon sống chung được viết lại thành các ca bị từ chối.
