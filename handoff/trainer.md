# Bàn giao: NTech Trainer

Bàn giao của Data Studio ở `handoff/latest.md`. Quy tắc làm song song ở `docs/README_AI_WORKFLOW.md`.

Làm trên nhánh `dev/trainer`, worktree `.claude/worktrees/trainer`.

## Trạng thái (2026-11-08)

Luồng dataset → train YOLO detect → ONNX đã kiểm chứng chạy thật bằng CLI và bằng app Desktop (M2, Avalonia), trên image CPU và dataset tổng hợp. Chưa chạy trên GPU hay dataset thật. App Desktop chưa chạy được trên Ubuntu ở máy này vì WSL thiếu thư viện X11. Thiết kế v0.4 ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md`, cách dùng ở `trainer/README.md`.

## Chủ dự án đã chốt

- Máy hiện tại chỉ để phát triển; máy train là Linux hoặc Windows cấu hình cao.
- TensorRT là tùy chọn theo profile máy. OCR chưa phải trọng tâm.
- Bộ phiên bản: YOLO26, YOLO11, YOLOv8, cùng YOLO12 thử nghiệm.
- 3 class wire harness: `dau_cos`, `day_thit`, `bang_dinh`.
- Hai tool ở chung repo, nhánh `dev/data-studio` và `dev/trainer`.

## Đã làm

- `trainer/python/ntech_worker`: kiểm tra dataset, preflight, train, đánh giá theo class, export và kiểm chứng ONNX, sự kiện, cảnh báo.
- `src/NTech.DataStudio.Trainer`: danh mục, đọc phần cứng, đánh giá, Docker, workspace, đọc sự kiện. Từ M2 thêm các use case dùng chung cho CLI và Desktop: `DatasetChecker`, `TrainPlanner`, `RunMonitor`, `EventText`, `RunSummary`, `RunStateTracker`, và `IContainerRuntime.RunningLabelValuesAsync` (một lần `docker ps` cho mọi run).
- `src/NTech.DataStudio.Trainer.Cli`: lệnh `probe`, `models`, `init`, `validate`, `train`, `status`, `follow`, `stop`, `resume`, `export`, `compare`. Nay gọi các use case trên thay cho logic riêng.
- `src/NTech.DataStudio.Trainer.Desktop` (M2): 5 màn hình Máy, Dataset, Cấu hình, Train, Kết quả. Token port từ WPF ở `Themes/Tokens`, style ở `Themes/Components`, biểu đồ tự vẽ ở `Controls/LineChart.cs`. Composition root ở `Bootstrap/AppComposition.cs`.
- `tests/NTech.DataStudio.Trainer.Desktop.Tests`: test ViewModel với Docker giả, dựng từng màn hình bằng Avalonia Headless và lưu PNG, so token với WPF, kiểm mọi `StaticResource` có tồn tại.
- `docker/trainer`: Dockerfile, file khóa phiên bản `lock-cpu.txt`, `lock-cuda.txt`, `update-locks.ps1`.
- Quyết định 0009 đến 0013.

## Đã kiểm tra

- Test: toàn solution 600 test C# xanh (Trainer 63, Trainer.Desktop 35), build 0 cảnh báo. Python 43, không đổi trong M2.
- Luồng CLI, chạy thật trên image CPU, imgsz 320, 80 ảnh tổng hợp với ảnh nền từ project wire harness:
  - YOLO26n, YOLO11n, YOLOv8n, YOLO12n đều train, đánh giá, export, và ONNX đạt kiểm chứng trên 6.000 box. YOLO11n có 2 box không có cặp, điểm cao nhất 0,045; các bản khác không có. IoU thấp nhất từ 0,99994. Số liệu ở mục 13 của thiết kế.
  - Dừng ở cuối epoch rồi train tiếp, kill rồi train tiếp, kill lúc preflight, chạy nền rồi `follow`, cổng xác nhận CPU, đường dẫn tiếng Việt, dataset không đổi một byte.
- App Desktop chạy thật trên Windows, điều khiển bằng UI Automation, Docker thật, workspace `app thật`, dataset `bộ dữ liệu`:
  - **Máy:** đọc đúng Quadro M2200 SM 5.2 là không dùng được, RAM Docker 7,7 GB là Weak, TensorRT báo cần SM 7.5.
  - **Dataset:** kiểm tra trong container, ra 60 ảnh train và 20 ảnh val.
  - **Cấu hình:** chọn YOLO26n, imgsz 320; nút Bắt đầu khóa cho tới khi tích xác nhận CPU, và đổi cỡ model thì phải tích lại.
  - **Train:** sau khi bắt đầu, app tự chuyển sang màn hình Train. Bấm dừng ở cuối epoch thì thành Đã dừng; bấm Train tiếp thì chạy tới Hoàn tất.
  - **Kết quả:** export ONNX đạt kiểm chứng (6.000 box, IoU thấp nhất 0,99994, 12,5 ms).
  - **Đóng app khi đang train:** container vẫn chạy; mở lại thì app nối vào run ở epoch 2/4 và theo tới Hoàn tất.
  - Dataset không đổi một byte, không còn container nào sau khi xong.
- Sau khi chuyển logic xuống phần lõi, CLI `compare` và `validate` vẫn chạy trên workspace đó; `compare` báo đúng hai run khác số epoch.

## Lỗi tìm được khi chạy thật

Test không bắt được, đã sửa, ghi lại để khỏi lặp.

**M1:**
- `ultralytics` không kéo theo `pyyaml`.
- Trộn GB và GiB.
- `settings.update` lỗi với khóa không có.
- Lượt đánh giá cuối bị tính là "Epoch 4/3" và sinh cảnh báo NaN giả.
- Export bị từ chối vì container chưa thoát hẳn.
- `resume` trả về ngay vì đọc trạng thái `Cancelled` cũ.
- `nms=False` là nhánh khác với metric.
- Box vượt khung ảnh.

**M2:**
- Cửa sổ cao hơn màn hình khi Windows để tỉ lệ 125%; nay mở toàn màn hình.
- Đường dẫn workspace dài đẩy các nút trên TopBar ra ngoài; nay cắt bớt và hiện đủ trong tooltip.
- Đổi phiên bản YOLO mà cỡ giữ nguyên `s` thì không đánh giá lại.
- Thông báo cũ còn lại sau khi run đã dừng hoặc đã xong.

Xem thêm `CLAUDE.md`.

## Chưa kiểm tra

- **App Desktop trên Ubuntu.** Bản Linux build được nhưng chưa mở được trong WSL vì thiếu `libice6`, `libsm6`, và `sudo` cần mật khẩu của chủ dự án. Sau khi chủ dự án cài (`sudo apt install libice6 libsm6`) thì chạy lại bằng WSLg. Chưa thử hộp chọn file trên Linux.
- **Dataset thật.** Lúc này chỉ có 2 ảnh có nhãn (48 box). Metric hiện có không nói lên chất lượng thật.
- **GPU.** Image `cuda` chưa build. VRAM đỉnh, batch tự động (`-1`), cảnh báo GPU, phần GPU trên màn hình Train, ONNX Runtime GPU đều chưa chạy.
- **Train từ phía Linux.** Docker Desktop chưa bật WSL Integration cho Ubuntu nên chưa chạy `train` từ Linux, và `--user` với quyền ghi chưa kiểm.
- **Hộp chọn file trên Windows.** Tạo workspace và mở workspace bằng hộp chọn file chỉ có test với hộp chọn giả; lần chạy thật mở workspace bằng thiết lập đã lưu.
- **Cảnh báo.** Lỗi hết VRAM, overfit, GPU nóng, sắp hết đĩa: chỉ có test logic.
- **Run dài.** Chưa chạy run dài, cỡ model lớn hay `imgsz` lớn. Các ngưỡng trong thiết kế vẫn là giả định.
- **Bên dùng ONNX.** Hợp đồng ONNX mới kiểm bằng code của chính repo này, chưa có bên dùng thật như NVision.

## Lưu ý

- Ultralytics nêu rõ model train ra thuộc AGPL-3.0 kể cả dùng nội bộ nếu không có Enterprise License, áp dụng cho mọi phiên bản trong gói. Chủ dự án chưa trả lời. Cần kết luận trước khi đưa model vào sản xuất hoặc bán.
- **Metric val ở epoch 2 trùng epoch 1.** Gặp ở mọi run 3 epoch trên máy này; run 5 epoch thì không. `results.csv` của chính Ultralytics cũng trùng, nên worker và app báo đúng số mà Ultralytics đưa ra. Nguyên nhân chưa rõ; xem lại khi có run dài trên máy train.
- Dữ liệu chạy thử nằm trong scratchpad của phiên, không nằm trong repo. Không commit ảnh của project wire harness.
- Image `ntech-trainer:dev-cpu` đang có trên máy, 2,65 GB.
- App lưu workspace gần nhất ở `%APPDATA%\NTechTrainer\settings.json` (Ubuntu: `~/.config/NTechTrainer/`).

## Tiếp theo

Xem `tasks/active.md`. Câu hỏi còn mở ở mục 14 của thiết kế.
