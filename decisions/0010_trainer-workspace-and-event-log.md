# 0010: Workspace là nguồn sự thật, giao tiếp với worker bằng log sự kiện

Ngày: 2026-11-08

`DESIGN_DOC` và thiết kế v0.1 dự kiến app nói chuyện với worker qua HTTP local hoặc REST và WebSocket, cùng SQLite lưu lịch sử job.

**Quyết định:**

1. Worker ghi `events.jsonl`, mỗi dòng một sự kiện có `schema`, `id`, `run`, `ts`, `type`, `data`. App đọc tiếp từ vị trí đã đọc và chỉ nhận dòng trọn vẹn.
2. Trạng thái job luôn suy ra từ sự kiện `state` cuối cùng cùng với việc container còn chạy hay không. Không lưu trạng thái ở nơi thứ hai.
3. App yêu cầu dừng bằng `control.json`, ghi kiểu đổi tên. Không dùng tín hiệu vì Windows và container xử lý khác nhau.
4. Lịch sử nằm trong file của workspace, không có SQLite và không đụng `project.db`.
5. Trainer nhận dataset YOLO bất kỳ có `data.yaml`. Bản xuất của Data Studio là một trường hợp. Dataset được mount chỉ đọc.

**Lý do:** job phải sống khi UI đóng, nên phải có bản ghi bền vững bên ngoài UI dù có dùng HTTP hay không. Không cần mở cổng thì cũng chưa cần xác thực. Dataset thật của chủ dự án có thể chưa nằm trong project, vì Data Studio chưa nhập được nhãn có sẵn.

**Hệ quả:**

- Đóng app, kill container, mất điện đều đọc lại được. Đã chạy thật với kill và với chạy nền rồi nối lại.
- Khi làm Remote Training Node thì thêm REST và WebSocket đọc từ cùng log, **bắt buộc có xác thực**. Hợp đồng sự kiện không đổi.
