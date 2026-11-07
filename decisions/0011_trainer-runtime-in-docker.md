# 0011: Runtime của Trainer nằm trong Docker, TensorRT là tùy chọn

Ngày: 2026-11-08

Máy hiện tại chỉ để phát triển. Máy train là Linux hoặc Windows cấu hình cao. TensorRT do chủ dự án chốt là tùy chọn với máy đủ điều kiện.

**Quyết định:**

1. Hai image khóa phiên bản: `cpu` và `cuda` (PyTorch cu132, cần GPU từ Turing trở lên). Không có image cho GPU cũ hơn; máy như vậy được chấm là không có GPU dùng được.
2. Image dùng chung cho mọi project. Mỗi job một container. Digest của image ghi vào `run.json`, và train tiếp bắt buộc đúng digest đó.
3. Train bằng CPU phải có xác nhận rõ (`--accept-risk`). Chỉ chặn hẳn khi chắc chắn không chạy được: không có Docker, đĩa dưới 5 GB. Ước tính VRAM là giả định nên chỉ yêu cầu xác nhận.
4. TensorRT không nằm trong luồng chính và không chặn ONNX. App báo khả năng theo compute capability của GPU.

**Hệ quả:**

- Cô lập khác ý ban đầu là đặt môi trường trong thư mục project: runtime ở image dùng chung, còn cấu hình và kết quả ở workspace. Tránh mỗi project tốn vài GB (image CPU 2,65 GB). Chủ dự án chưa xác nhận điểm này.
- Docker Desktop trên Windows có điều khoản trả phí với công ty lớn. Nếu vướng thì Windows cần backend khác, ví dụ venv; `IContainerRuntime` là chỗ tách.
- Container là Linux, nên engine TensorRT build trong đó mặc định không chạy trên Windows. Xử lý khi làm M5.
