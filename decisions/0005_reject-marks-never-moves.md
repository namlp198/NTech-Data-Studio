# 0005: Loại ảnh chỉ đánh dấu, không di chuyển file

Ngày: 2026-11-06

**Quyết định:** ảnh bị loại được đánh dấu trong `source_images`. Thư mục `rejected/` chỉ chứa `rejected.csv` liệt kê file và lý do. Không file ảnh nào bị xóa, di chuyển hay chép vào đó.

**Thay đổi so với tài liệu thiết kế:** thiết kế ghi "kết quả đưa vào `rejected/`". Làm đúng nghĩa đen thì hoặc phải di chuyển file gốc, trái với nguyên tắc ảnh gốc không bị sửa và với chế độ chỉ tham chiếu đường dẫn, hoặc phải chép thêm, có thể tới hàng chục GB.

**Hệ quả:** khôi phục ảnh chỉ là đổi trạng thái. Ảnh đã khôi phục được ghi nhớ (`keep_override`), nên lần lọc sau không loại lại. File hỏng thì không khôi phục được.
