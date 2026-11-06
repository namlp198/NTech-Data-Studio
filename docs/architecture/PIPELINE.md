# Pipeline

```
import → audit → analyze + clean → normalize → annotate → split → preprocess → augment (chỉ train) → validate → export
```

| Giai đoạn | Đầu vào | Đầu ra | Trạng thái |
| --- | --- | --- | --- |
| Import | Thư mục ảnh | `source_images`, `source/` nếu chọn chép | có |
| Audit | Header đã đọc | Báo cáo trên màn hình | có |
| Analyze, clean | File gốc | Hash và chỉ số chất lượng, `thumbs/`, `rejected/rejected.csv` | có |
| Normalize | Ảnh gốc không bị loại, model profile | `images`, `master/`, `thumbs/` | có |
| Annotate | Ảnh master | `annotations`, `image_tags`, trạng thái ảnh | có |
| Split, preprocess, augment | Ảnh master và nhãn | `versions`, `version_items` | chưa làm |
| Validate, export | Một version và một model profile | `exports/<version>_<format>/` | chưa làm |

Ảnh tăng cường và dataset xuất ra luôn sinh lại được từ master, config và `seed`.

## Xử lý song song

Analyze và normalize chạy `Parallel.ForEach`. Số luồng là số nhỏ hơn giữa số nhân CPU và 50% RAM còn trống chia cho bộ nhớ ước tính của ảnh lớn nhất (`ParallelismPolicy`). Kết quả được lưu theo từng ảnh, nên hủy hoặc mất điện giữa chừng thì chạy lại sẽ làm tiếp. Master được ghi qua file `.partial` rồi đổi tên.

## Đọc ghi ảnh

Mọi file đi qua `byte[]`, vì `Cv2.ImRead` và `ImWrite` không mở được đường dẫn tiếng Việt trên Windows. Đừng đổi sang hai hàm đó.
