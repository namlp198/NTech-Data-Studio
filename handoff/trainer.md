# Bàn giao: NTech Trainer

Bàn giao của Data Studio ở `handoff/latest.md`. Quy tắc làm song song ở `docs/README_AI_WORKFLOW.md`.

Làm trên nhánh `dev/trainer`, worktree `.claude/worktrees/trainer`.

## Trạng thái (2026-11-08)

Luồng dataset → train YOLO detect → ONNX đã kiểm chứng chạy thật bằng CLI, trên image CPU và dataset tổng hợp. Chưa có Desktop. Chưa chạy trên GPU hay dataset thật. Thiết kế v0.4 ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md`, cách dùng ở `trainer/README.md`.

## Chủ dự án đã chốt

- Máy hiện tại chỉ để phát triển; máy train là Linux hoặc Windows cấu hình cao.
- TensorRT là tùy chọn theo profile máy. OCR chưa phải trọng tâm.
- Bộ phiên bản: YOLO26, YOLO11, YOLOv8, cùng YOLO12 thử nghiệm.
- 3 class wire harness: `dau_cos`, `day_thit`, `bang_dinh`.
- Hai tool ở chung repo, nhánh `dev/data-studio` và `dev/trainer`.

## Đã làm

- `trainer/python/ntech_worker`: kiểm tra dataset, preflight, train, đánh giá theo class, export và kiểm chứng ONNX, sự kiện, cảnh báo.
- `src/NTech.DataStudio.Trainer` và `.Cli`: danh mục, đọc phần cứng, đánh giá, Docker, workspace, đọc sự kiện. Lệnh: `probe`, `models`, `init`, `validate`, `train`, `status`, `follow`, `stop`, `resume`, `export`, `compare`.
- `docker/trainer`: Dockerfile, file khóa phiên bản `lock-cpu.txt`, `lock-cuda.txt`, `update-locks.ps1`.
- Quyết định 0009 đến 0012.

## Đã kiểm tra

- Test: C# 56, Python 43. Toàn solution xanh (558 test C#), build 0 cảnh báo.
- Chạy thật trên image CPU, imgsz 320, 80 ảnh tổng hợp, ảnh nền từ project wire harness:
  - YOLO26n, YOLO11n, YOLOv8n, YOLO12n: train, đánh giá, export, ONNX đạt kiểm chứng trên 6.000 box. YOLO11n có 2 box không có cặp, điểm cao nhất 0,045; các bản khác không có. IoU thấp nhất từ 0,99994. Số liệu ở mục 13 của thiết kế.
  - Dừng ở cuối epoch rồi train tiếp: tiếp nối đúng epoch. Kill sau epoch đầu thành `Interrupted` rồi train tiếp: tiếp nối đúng. Kill lúc preflight: từ chối và nêu lý do.
  - Chạy nền rồi `follow`. Không xác nhận thì không train bằng CPU. Dataset và workspace có đường dẫn tiếng Việt. Dataset không đổi một byte, kiểm bằng hash toàn bộ file trước và sau.
- Bản Linux tự chứa trong WSL Ubuntu: `models`, `init`, `probe`, và báo đúng khi không có Docker.
- `model.meta.json` ghi đúng cờ `synthetic_dataset`, fingerprint, digest (đọc lại sau khi sửa).

## Lỗi tìm được khi chạy thật

Test không bắt được, đã sửa, ghi lại để khỏi lặp: `ultralytics` không kéo theo `pyyaml`; trộn GB và GiB; `settings.update` lỗi với khóa không có; lượt đánh giá cuối bị tính là "Epoch 4/3" và sinh cảnh báo NaN giả; export bị từ chối vì container chưa thoát hẳn; `resume` trả về ngay vì đọc trạng thái `Cancelled` cũ; `nms=False` là nhánh khác với metric; box vượt khung ảnh. Xem thêm `CLAUDE.md`.

## Chưa kiểm tra

- **Dataset thật.** Lúc này chỉ có 2 ảnh có nhãn (48 box). Metric hiện có không nói lên chất lượng thật.
- **GPU.** Image `cuda` chưa build. VRAM đỉnh, batch tự động (`-1`), cảnh báo GPU, ONNX Runtime GPU đều chưa chạy.
- **Train từ phía Linux.** Docker Desktop chưa bật WSL Integration cho Ubuntu nên chưa chạy `train` từ Linux, và `--user` với quyền ghi chưa kiểm.
- Lỗi hết VRAM, overfit, GPU nóng, sắp hết đĩa: chỉ có test logic.
- Run dài, cỡ model lớn, `imgsz` lớn. Các ngưỡng trong thiết kế vẫn là giả định.
- Hợp đồng ONNX mới kiểm bằng code của chính repo này, chưa có bên dùng thật như NVision.

## Lưu ý

- Ultralytics nêu rõ model train ra thuộc AGPL-3.0 kể cả dùng nội bộ nếu không có Enterprise License, áp dụng cho mọi phiên bản trong gói. Chủ dự án chưa trả lời. Cần kết luận trước khi đưa model vào sản xuất hoặc bán.
- Dữ liệu chạy thử nằm trong scratchpad của phiên, không nằm trong repo. Không commit ảnh của project wire harness.
- Image `ntech-trainer:dev-cpu` đang có trên máy, 2,65 GB.

## Tiếp theo

Xem `tasks/active.md`. Câu hỏi còn mở ở mục 14 của thiết kế.
