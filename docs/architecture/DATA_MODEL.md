# Mô hình dữ liệu

## Thư mục project

```
MyProject/
  project.db    SQLite
  source/       ảnh gốc khi chọn chép vào project, không sửa
  master/       ảnh đã chuẩn hóa, dùng để gán nhãn
  thumbs/       thumbnail 256 px: source_<id>.jpg của ảnh gốc, <tên master>.jpg của master
  rejected/     rejected.csv: danh sách ảnh bị loại và lý do
  versions/     snapshot config của từng version
  exports/      dataset đã xuất
```

Mở project sẽ tạo lại các thư mục con bị thiếu.

## Bảng SQLite

Schema nằm ở `src/NTech.DataStudio.Storage/Sqlite/Migrations/ProjectMigrations.cs`.

| Bảng | Trường chính |
| --- | --- |
| `project_meta` | key, value (`name`, `created_at`, `normalize_config`) |
| `source_images` | id, original_path, stored_path, is_copied, file_size, modified_ticks, format, width, height, bit_depth, channels, group_key, sha256, phash, dhash, blur_score, dark_fraction, bright_fraction, reject_reason, reject_detail, keep_override |
| `images` | id, source_image_id, master_path, thumb_path, width, height, origin_x, origin_y, scale, file_size, status |
| `classes` | id, name, color, hotkey, sort_order |
| `annotations` | id, image_id, class_id, kind, points_json, source, created_at, updated_at |
| `image_tags` | image_id, class_id |
| `versions` | id, name, prep_json, split_json, augment_json, seed, created_at |
| `version_items` | version_id, image_id, split |

View `active_images`: master còn dùng được, tức ảnh nguồn không bị loại và master không ở trạng thái `rejected`. Mọi truy vấn phục vụ gán nhãn và xuất dataset nên đọc từ view này.

## Khác với tài liệu thiết kế

- Thêm `project_meta` để lưu tên project và cấu hình chuẩn hóa.
- Tách `source_images` khỏi `images` ([quyết định 0004](../../decisions/0004_source-images-separate-from-masters.md)).
- `classes.order` đổi thành `sort_order`, vì `order` là từ khóa SQL.

## Quy tắc

- `annotations.id` là GUID, để undo thêm lại nhãn mà vẫn giữ nguyên id.
- Box và RotatedBox lưu 4 góc theo thứ tự vẽ; Polygon lưu các đỉnh; Point lưu 1 điểm. Tọa độ là pixel trên master. Điểm trên ảnh gốc quy sang master theo `(x - origin_x) * scale`.
- `source_images.stored_path` là đường dẫn tương đối khi ảnh được chép vào project, tuyệt đối khi chỉ tham chiếu. Project dùng ảnh tham chiếu thì chép sang máy khác sẽ báo thiếu file gốc khi cần đọc lại; master đã tạo vẫn dùng được.
- Import nhận diện ảnh đã có theo đường dẫn, dung lượng và thời điểm sửa. File đổi nội dung được coi là ảnh mới.
- `phash` và `dhash` là số 64-bit không dấu, lưu dạng `INTEGER` có dấu theo cùng dãy bit.
- Xóa ảnh thì xóa luôn nhãn của ảnh. `classes` có khóa RESTRICT: chỉ `ClassService.Delete` xóa được class, và nó xóa nhãn, tag của class trước.
- `images.status`: Annotate đổi theo quy tắc ở `docs/design/screens/ANNOTATE.md` (`StatusRules`). Ảnh chưa có nhãn và không Null thì là `new`.
- `annotations.points_json` là `[[x,y],...]`, số thực ghi và đọc lại không mất độ chính xác. Box và OBB luôn đủ 4 điểm; `updated_at` đổi khi sửa, `created_at` giữ nguyên.
- `versions`: `prep_json`, `split_json`, `augment_json` là `PreprocessConfig`, `SplitConfig`, `AugmentConfig` dạng JSON (enum ghi bằng tên), `seed` là seed chung. Trường mới thêm vào config sau này đọc được từ version cũ vì thiếu trường thì lấy mặc định. `version_items` ghi ảnh nào thuộc tập nào lúc tạo version; không tự cập nhật khi nhãn hay ảnh đổi ([quyết định 0007](../../decisions/0007_version-freezes-membership-and-config-not-pixels.md)). Thư mục `versions/` chưa được ghi.
- `PRAGMA user_version` là số migration đã áp dụng. Project có version mới hơn app thì bị từ chối và không bị sửa.
- WAL với `synchronous = FULL`: dữ liệu đã commit không mất khi mất điện. Connection không dùng pool, nên đóng connection cuối là không còn file `-wal`, chép thư mục đi được ngay.
