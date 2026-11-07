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
| Split, preprocess, augment | Ảnh master và nhãn | `versions`, `version_items` (chỉ config và danh sách; không ghi ảnh) | có |
| Validate, export | Một version và một model profile | `exports/<version>_<format>/` | chưa làm |

Ảnh tăng cường và dataset xuất ra luôn sinh lại được từ master, config và `seed` ([quyết định 0007](../../decisions/0007_version-freezes-membership-and-config-not-pixels.md)).

## Tạo version (Module 3)

```
ảnh active → lọc trạng thái → remap class → bỏ ảnh không nhãn theo tỉ lệ → chia tập → (xuất) RenderPlan → ảnh + nhãn
```

- `VersionBuilder.Build` làm các bước đến chia tập và tính số liệu, không ghi gì. `DatasetService.CreateVersion` ghi `versions` và `version_items` trong một transaction.
- Mỗi ảnh xuất ra là một `RenderPlan`: kích thước đầu ra, **một** ma trận affine từ pixel master sang pixel đầu ra (`RenderPlanner`: preprocessing resize rồi các phép hình học của tăng cường, gộp lại), và các thông số ánh sáng, màu, nhiễu. `OpenCvAugmentationEngine` warp ảnh một lần theo ma trận đó; `LabelTransformer` đưa nhãn qua cùng ma trận. Ảnh và nhãn đọc cùng một `RenderPlan` nên không lệch nhau.
- Ma trận ở tọa độ cạnh (0..W), cùng hệ với nhãn. OpenCV địa chỉ pixel theo tâm, nên trước khi warp phải qua `Affine2D.ToPixelCenters()`; bỏ bước này thì ảnh lệch nhãn nửa pixel. Test engine kiểm tra sai số dưới 1 px cho từng phép.
- Thu nhỏ mạnh (hệ số dưới 0,5): engine thu nhỏ bằng INTER_AREA trước, rồi warp phần còn lại, vì `warpAffine` không lọc và sẽ răng cưa.
- Mẫu tăng cường là hàm của `(seed, imageId, copyIndex)` qua `DeterministicRandom`. Bản 0 là ảnh gốc, không bị chạm tới.
- Chia tập (`SplitService`) chỉ phụ thuộc đầu vào, config và seed, không phụ thuộc thứ tự đầu vào.

## Xử lý song song

Analyze và normalize chạy `Parallel.ForEach`. Số luồng là số nhỏ hơn giữa số nhân CPU và 50% RAM còn trống chia cho bộ nhớ ước tính của ảnh lớn nhất (`ParallelismPolicy`). Kết quả được lưu theo từng ảnh, nên hủy hoặc mất điện giữa chừng thì chạy lại sẽ làm tiếp. Master được ghi qua file `.partial` rồi đổi tên.

## Đọc ghi ảnh

Mọi file đi qua `byte[]`, vì `Cv2.ImRead` và `ImWrite` không mở được đường dẫn tiếng Việt trên Windows. Đừng đổi sang hai hàm đó.
