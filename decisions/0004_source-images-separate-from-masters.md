# 0004: Tách ảnh nguồn khỏi ảnh master

Ngày: 2026-11-06

**Quyết định:** dữ liệu ảnh được tách thành 2 bảng. `source_images` giữ file gốc, hash và kết quả làm sạch. `images` giữ từng ảnh master, là thứ được gán nhãn.

**Lý do:** thiết kế gốc gom tất cả vào `images` với một `source_path` và một `master_path`. Khi tile, một ảnh gốc sinh ra nhiều master, nên một dòng không chứa đủ. Ảnh chưa chuẩn hóa cũng chưa có master.

**Hệ quả:**

- Mỗi master lưu `origin_x`, `origin_y`, `scale` để quy tọa độ về ảnh gốc.
- View `active_images` loại master của ảnh nguồn đã bị loại. Loại một ảnh nguồn không đụng tới nhãn của nó, nên khôi phục là có lại đủ.
- `group_key` nằm ở `source_images`; các tile của một ảnh luôn cùng nhóm khi chia tập.
