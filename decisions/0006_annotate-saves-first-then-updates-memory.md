# 0006: Mỗi sửa nhãn ghi DB trước, rồi mới đổi bộ nhớ

Ngày: 2026-11-06

**Quyết định:** mọi thay đổi nhãn đi qua `AnnotateService.Execute`. Nó ghi vào `project.db` trong một transaction (WAL, `synchronous = FULL`), và chỉ khi ghi xong mới đổi `ImageDocument` trong bộ nhớ và đẩy vào lịch sử undo. Ghi lỗi thì tài liệu và lịch sử giữ nguyên, người dùng thấy thông báo.

**Lý do:** thiết kế yêu cầu "tắt app đột ngột không mất nhãn". Nếu màn hình chỉ hiện những gì đã nằm trong DB thì không có khoảng nào nhãn đã hiện mà chưa lưu.

**Hệ quả:**

- Mỗi edit lưu `Before` và `After` của các nhãn bị ảnh hưởng cùng trạng thái ảnh trước và sau. Hoàn tác là ghi ngược lại, nên cũng được lưu ngay. Nhãn giữ nguyên `id` qua undo và redo.
- Kéo hình chỉ là xem trước; edit chỉ được ghi khi nhả chuột, nên một lần kéo là một bước undo.
- Ghi chạy đồng bộ trên luồng giao diện. Đã đo bằng harness: 200 nhãn trong một edit mất 60 ms. Nếu ổ chậm gây giật khi vẽ liên tục, phải chuyển sang hàng đợi ghi có thứ tự, và phải xem lại cam kết trên.
- Màn hình Annotate giữ một connection chỉ trong lúc đang hiển thị (`IActivatable`), nên Prepare và việc chép thư mục project không bị chặn.
