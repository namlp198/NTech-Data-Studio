# 0014. Project khai báo task mục tiêu (detect, segment, OBB, classify)

Ngày: 2026-11-08

## Bối cảnh

Nhãn detect (box) và nhãn segment (polygon) khác nhau về bản chất: polygon cho ra box được, box không cho ra polygon. Nếu chỉ chọn task lúc xuất, người dùng có thể gán nhãn cả nghìn ảnh bằng box rồi mới biết cần segment, phải vẽ lại. Trước đây app không có khái niệm task ở cấp project; chỉ profile (`ModelProfile.Task`) mới biết, và chỉ lúc xuất.

## Quyết định

1. **Project có một task mục tiêu** (`TaskKind`: Detect, Segment, Obb, Classify), lưu trong bảng `project_meta`, khóa `target_task`. Không thêm migration. Project cũ không có khóa này: task chưa đặt, mọi công cụ vẫn dùng được như trước.
2. **Chọn khi tạo project, đổi được sau.** Đổi không xóa hay sửa nhãn nào; chỉ đổi công cụ được bật và kết quả kiểm tra.
3. **Annotate chỉ bật công cụ vẽ phù hợp:** Detect → Box; Segment → Polygon; Obb → Rotated box; Classify → Tag. Select và Pan luôn bật. Q/Shift+Q và phím tắt bỏ qua công cụ bị tắt. Nhãn đã có thuộc loại khác vẫn hiện và sửa được, không bị xóa.
4. **Export kiểm tra khớp, không chặn.** Profile có task khác task mục tiêu thì báo cảnh báo (không phải lỗi), vì xuất Segment → Detect là hợp lệ (box suy ra từ polygon, `LabelShapes`). Lỗi thật vẫn do các kiểm tra sẵn có bắt (ví dụ ảnh chỉ có box mà profile là Segment). Màn hình Export chọn sẵn profile cùng task với project.
5. **`version.json` ghi `targetTask` của project** lúc xuất, bên cạnh task của profile, để biết dataset xuất ra nhằm mục đích gì.

## Không làm (cố ý)

- Không khóa task ở cấp version trong DB: version đóng băng danh sách ảnh, split, config (quyết định 0007), còn task của bản xuất do profile quyết định lúc xuất. Một version xuất được nhiều profile.
- Không tự chuyển nhãn box thành polygon hay ngược lại khi đổi task.
- Anomaly không phải task mục tiêu của project (không có nhãn).

## Hệ quả

- Hướng an toàn khi chưa chắc nhu cầu là chọn Segment: polygon xuất được cả Segment lẫn Detect. Cái giá là gán nhãn chậm hơn.
- Muốn cho phép vẽ nhiều loại cùng lúc thì để task chưa đặt (project cũ), hoặc đổi task giữa chừng.
