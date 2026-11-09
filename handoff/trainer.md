# Bàn giao: NTech Trainer

Bàn giao của Data Studio ở `handoff/latest.md`. Quy tắc làm song song ở `docs/README_AI_WORKFLOW.md`.

Làm trên nhánh `dev/trainer`, worktree `.claude/worktrees/trainer`.

## Trạng thái (2026-11-09)

Luồng dataset → train YOLO detect → ONNX đã kiểm chứng chạy thật bằng CLI và bằng app Desktop (M2, Avalonia), trên Windows và trên Ubuntu 24.04 (WSLg), với image CPU và dataset tổng hợp. Đã chạy trên GPU (RTX 4060 Ti, image CUDA) với dataset tổng hợp; chưa chạy dataset thật. Thiết kế v0.4 ở `docs/trainer/TRAINER_SYSTEM_DESIGN.md`, cách dùng ở `trainer/README.md`.

**Segment (2026-11-09, quyết định 0014):** thêm `task` vào danh mục và `run.json`/`config.yaml`; file `trainer/catalog/ultralytics-segment.yaml` với `yolo26seg`, `yolo11seg`, `yolov8seg` (đặt tên model `yolo26segn`...). Worker kiểm tra dataset theo task (segment gặp box là lỗi), báo metric mask, xuất ONNX hai output (box kèm hệ số mask, proto) và kiểm chứng cả mask. Màn hình Cấu hình có ô chọn tác vụ. Đã chạy thật bằng CLI trên GPU, dataset tổng hợp 320 px, 40 epoch, cả ba họ qua train, export và kiểm chứng ONNX (bảng số ở quyết định). Cả ba ghi `experimental`: chưa có dataset polygon thật, chưa đo VRAM (đánh giá Unknown). Bẫy: `predict` của Ultralytics trả tập box khác NMS nhúng với segment không-end2end, nên kiểm chứng đối chiếu mô hình thô; nguyên nhân chưa rõ.

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
- `scripts/trainer` (2026-11-08): `build`, `deploy`, `build-image`, mỗi lệnh có `.ps1`, `.bat` và `.sh`; `package` (`.ps1`, `.bat`) build installer Inno Setup từ `trainer/installer/NTech-Trainer.iss`, AppId riêng `{374AE65E-1993-4389-BA78-DDA65F15E499}`. Trên Windows, `deploy` ra installer cho win-x64 và `.tar.gz` cho Linux; `deploy.sh` ra `.tar.gz` và `.zip`. Gói nào cũng có app, CLI và thư mục `image/` để build image trên máy train không cần repo; `-SaveImage` lưu kèm file image. Cách dùng ở `trainer/README.md`.
- Quyết định 0009 đến 0013.

## Đã kiểm tra

- Test: toàn solution 602 test C# xanh (Trainer 64, Trainer.Desktop 36), build 0 cảnh báo. Python 43, không đổi trong M2.
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
- App Desktop chạy thật trên Ubuntu 24.04 trong WSLg (2026-11-08), sau khi chủ dự án cài `libice6`, `libsm6`:
  - **Giao diện:** Inter hiện đủ dấu tiếng Việt. Máy đọc đúng phần cứng nhìn từ WSL (RAM 7,7 GB của máy ảo WSL, Quadro qua nvidia-smi).
  - **Run tạo trên Windows:** app đọc được các run đó qua `/mnt/c`.
  - **Train từ phía Linux:** workspace trên đĩa Linux (`~/…`), dataset đọc từ `/mnt/c`; CLI Linux bắt đầu train YOLO26n 3 epoch.
  - **Theo dõi và export:** app Linux theo dõi run tới Hoàn tất, rồi export ONNX đạt kiểm chứng (6.000 box, IoU thấp nhất 0,99994, 16 ms).
  - **Quyền ghi:** container chạy với `--user 1000:1000`, và cả 29 file kết quả thuộc user Linux. Đây là điểm trước đó chưa kiểm.
  - Không còn container nào sau khi xong. File thử trong WSL đã xóa.

- Script đóng gói chạy thật (2026-11-08). Máy lúc này đọc ra RTX 4060 Ti 16 GB (SM 8.9), WSL là Ubuntu 26.04:
  - `deploy.bat -Runtime win-x64,linux-x64`: 100 test Trainer xanh, ra installer 50,5 MB và tar.gz 44 MB. Test Python bị bỏ qua vì máy không có `uv`, script cảnh báo rõ. (Trước khi có installer, gói zip 71 MB cũng đã chạy được.)
  - **Installer:** cài `/VERYSILENT /CURRENTUSER /DIR=` vào thư mục tạm có dấu cách; có shortcut Start Menu và khóa gỡ cài đặt. `ntech-trainer.exe models` và app đã cài chạy; `image\build-image.bat` trong thư mục cài build lại được image. Gỡ ra thì thư mục, khóa registry và shortcut đều mất.
  - **Gói Windows:** giải nén, `ntech-trainer.exe models` chạy, app Desktop mở cửa sổ.
  - **Gói Linux tạo trên Windows:** `sha256sum -c` khớp; `NTechTrainer`, `ntech-trainer`, `createdump`, `build-image.sh` có quyền 0755. Trong WSL, `models` và `probe` chạy (với `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`, vì WSL này thiếu `libicu`).
  - **Image từ gói:** `image/build-image.sh --compute cpu` trong gói đã giải nén build ra `ntech-trainer:dev-cpu`.
  - **`-SaveImage cpu -Destination`:** file image 582 MB, chép đi khớp hash, `docker load` lại được và import được `ntech_worker`.
  - **`deploy.sh` trong Git Bash trên Windows:** ra cả hai gói, chép đi được; đích có file khác nội dung thì từ chối và thoát mã 1.

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
- **Lỗi bị xóa trước khi người dùng thấy.** Export hay Train tiếp thất bại thì thông báo lỗi bị lượt làm mới 2 giây sau xóa mất, cả trên Windows. Nay lượt làm mới chỉ xóa lỗi do chính nó gây ra.
- **Workspace tạo trên Windows mở trên Linux.** `workspace.json` ghi `C:\…\data.yaml`, nên trên Linux Docker nhận nguồn mount rỗng. Nay train, export và kiểm tra dataset từ chối trước khi gọi Docker, kèm hướng dẫn tạo workspace mới trên máy đó.

**Script đóng gói:**
- `tar` của Windows không đặt được quyền thực thi từ NTFS, nên gói Linux tạo trên Windows mất bit `x`. Nay `deploy.ps1` mô tả từng file trong manifest mtree.
- `python3` trên PATH có thể chỉ là lối tắt Microsoft Store: `command -v` thấy nhưng không chạy. Nay `deploy.sh` chạy thử trước khi dùng nó để nén zip.

Xem thêm `CLAUDE.md`.

## Chưa kiểm tra

- **Segment trên dữ liệu thật.** Chỉ chạy dataset tổng hợp (tròn và vuông). Chưa train polygon thật, chưa đo VRAM ở 640. Màn hình Cấu hình mới được kiểm bằng test giao diện không màn hình (ScreenTests và test ViewModel), chưa mở app thật để nhìn ô chọn tác vụ.
- **Segment trong app.** Mới chạy luồng train, export bằng CLI. Chưa chạy một run segment từ app Desktop.

- **Docker Desktop trên Ubuntu không dùng được cho GPU (2026-11-08).** Máy Ubuntu 24.04 thật có RTX 5090 dùng Docker Desktop: RAM Docker chỉ 7,7 GB trên 60,5 GB, không có runtime nvidia, nên màn hình Máy báo Weak. Cách xử lý (gỡ Desktop, cài Docker Engine và NVIDIA Container Toolkit) đã ghi ở `docs/trainer/DOCKER_LINUX.md`. Người dùng làm xong bước cài nhưng kết quả `nvidia-smi` trong container và "Kiểm tra trong container" chưa được ghi lại. Trainer chưa cảnh báo khi gặp Docker Desktop trên Linux, và tiêu chí GPU vẫn báo Excellent dù container không thấy GPU.
- **`deploy.sh` trên Linux thật.** Mới chạy trong Git Bash trên Windows; WSL này chưa có .NET SDK. App Desktop từ gói Linux chưa mở (WSL thiếu `libicu`, `libice6`, `libsm6`). `-SaveImage cuda` chưa chạy.

- **Ubuntu thật.** Mới chạy trong WSLg, chưa chạy trên máy Ubuntu thật (Docker Engine và NVIDIA Container Toolkit). Trong WSLg cửa sổ không có cây UI Automation, nên phần nhập liệu ở Cấu hình được làm bằng CLI Linux; app Linux chỉ được bấm chuyển tab và Export ONNX.
- **App Linux tự thoát một lần.** Một lần app Linux thoát với mã 0, log rỗng, trong khoảng export. Làm lại đúng các bước thì không bị. Chưa rõ nguyên nhân; thử lại trên Ubuntu thật.
- **Hộp chọn file trên Linux.** Chưa thử.
- **Dataset thật.** Lúc này chỉ có 2 ảnh có nhãn (48 box). Metric hiện có không nói lên chất lượng thật.
- **GPU.** Image `cuda` chưa build. VRAM đỉnh, batch tự động (`-1`), cảnh báo GPU, phần GPU trên màn hình Train, ONNX Runtime GPU đều chưa chạy.
- **Hộp chọn file trên Windows.** Tạo workspace và mở workspace bằng hộp chọn file chỉ có test với hộp chọn giả; lần chạy thật mở workspace bằng thiết lập đã lưu.
- **Cảnh báo.** Lỗi hết VRAM, overfit, GPU nóng, sắp hết đĩa: chỉ có test logic.
- **Run dài.** Chưa chạy run dài, cỡ model lớn hay `imgsz` lớn. Các ngưỡng trong thiết kế vẫn là giả định.
- **Bên dùng ONNX.** Hợp đồng ONNX mới kiểm bằng code của chính repo này, chưa có bên dùng thật như NVision.

## Lưu ý

- Ultralytics nêu rõ model train ra thuộc AGPL-3.0 kể cả dùng nội bộ nếu không có Enterprise License, áp dụng cho mọi phiên bản trong gói. Chủ dự án chưa trả lời. Cần kết luận trước khi đưa model vào sản xuất hoặc bán.
- **Metric val ở epoch 2 trùng epoch 1.** Gặp ở mọi run 3 epoch trên máy này; run 5 epoch thì không. `results.csv` của chính Ultralytics cũng trùng, nên worker và app báo đúng số mà Ultralytics đưa ra. Nguyên nhân chưa rõ; xem lại khi có run dài trên máy train.
- Dữ liệu chạy thử nằm trong scratchpad của phiên, không nằm trong repo. Không commit ảnh của project wire harness.
- Image `ntech-trainer:dev-cpu` đang có trên máy, 2,66 GB, build lại ngày 2026-11-08 bằng `scripts/trainer`.
- App lưu workspace gần nhất ở `%APPDATA%\NTechTrainer\settings.json` (Ubuntu: `~/.config/NTechTrainer/`).

## Tiếp theo

Xem `tasks/active.md`. Câu hỏi còn mở ở mục 14 của thiết kế.
