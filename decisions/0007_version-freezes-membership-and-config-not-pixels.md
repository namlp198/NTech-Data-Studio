# 0007: Version đóng băng danh sách ảnh, split và config, không đóng băng điểm ảnh

Ngày: 2026-11-07

**Quyết định:** tạo Version chỉ ghi vào `project.db`: một dòng `versions` (config preprocess, split, augment dạng JSON, cùng `seed`) và các dòng `version_items` (ảnh nào vào tập nào). Module 3 không ghi ảnh tăng cường ra đĩa. Ảnh tăng cường được sinh khi xuất dataset (Module 4), từ ảnh master, config và `seed` của version.

**Lý do:** thiết kế viết "ảnh tăng cường và dataset xuất ra luôn sinh lại được từ master và config". Lưu sẵn ảnh tăng cường nhân số ảnh lên 2 đến 10 lần mà không thêm thông tin, và dễ lệch khỏi config. Giữ `DatasetConfig` + seed thì cùng một version luôn ra cùng một bộ ảnh.

**Hệ quả:**

- Version đóng băng *ai thuộc tập nào* và *cách biến đổi*, không đóng băng nhãn. Nhãn được đọc từ `annotations` lúc xuất. Sửa nhãn sau khi tạo version làm bản xuất sau khác bản xuất trước ở nhãn, không ở split. Ảnh gán thêm sau đó không vào version cũ; muốn có thì tạo version mới.
- Mọi số ngẫu nhiên dùng `DeterministicRandom` (SplitMix64) chứ không dùng `System.Random`, vì .NET không hứa cùng seed ra cùng dãy số giữa các bản runtime. Dãy số của một mẫu tăng cường là hàm của `(seed, imageId, copyIndex)`, và mọi giá trị được rút theo thứ tự cố định dù tùy chọn tương ứng bật hay tắt, nên bật thêm một phép không làm đổi kết quả của các phép khác. Test `Sample_PinnedValuesDoNotDriftBetweenRuntimeVersions` ghim một giá trị; nếu phải sửa nó thì mọi version đã lưu sẽ sinh ra bộ ảnh khác, và đó là một thay đổi phá vỡ.
- Nhiễu Gaussian cũng lấy từ `DeterministicRandom` (tổng 4 số 16-bit, phương sai 1/3) chứ không dùng bộ sinh toàn cục của OpenCV, để kết quả lặp lại được kể cả khi nhiều luồng cùng xuất ảnh.
- Thư mục `versions/` trong project chưa được ghi. `project.db` là nguồn duy nhất của config; nếu sau này cần file snapshot để đọc bằng mắt thì phải sinh từ DB, không phải nguồn thứ hai.
- Xóa version là `DELETE FROM versions`; `version_items` xóa theo (ON DELETE CASCADE). Ảnh, nhãn và version khác không bị ảnh hưởng.
- Cột `versions.name` là UNIQUE. Tên trùng bị từ chối và không để lại dòng nào.
