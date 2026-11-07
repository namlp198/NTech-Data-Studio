# NTech Trainer

Train YOLO detect trong Docker và xuất ONNX đã kiểm chứng. Dùng bằng app Desktop (Avalonia) hoặc dòng lệnh; cả hai gọi cùng một phần lõi và cùng đọc một workspace. Thiết kế ở [TRAINER_SYSTEM_DESIGN.md](../docs/trainer/TRAINER_SYSTEM_DESIGN.md).

Model train bằng gói `ultralytics` thuộc AGPL-3.0 kể cả dùng nội bộ theo lập trường của Ultralytics, trừ khi có Enterprise License. Xem mục 10 của thiết kế trước khi đưa model vào sản xuất hoặc bán.

## Build

Chạy từ gốc repo. Máy cần Docker.

```powershell
docker build -f docker/trainer/Dockerfile --build-arg COMPUTE=cpu -t ntech-trainer:dev-cpu .
```

Máy train có GPU NVIDIA từ Turing trở lên (chưa chạy thử):

```powershell
docker build -f docker/trainer/Dockerfile --build-arg COMPUTE=cuda --build-arg TORCH_INDEX=https://download.pytorch.org/whl/cu132 -t ntech-trainer:dev-cuda .
```

Phần C#:

```powershell
dotnet build NTech.DataStudio.Trainer.slnf -c Debug
dotnet test NTech.DataStudio.Trainer.slnf
```

Bản Linux tự chứa:

```powershell
dotnet publish src\NTech.DataStudio.Trainer.Cli -c Release -r linux-x64 --self-contained true -o publish\trainer-linux
dotnet publish src\NTech.DataStudio.Trainer.Desktop -c Release -r linux-x64 --self-contained true -o publish\trainer-desktop-linux
```

App Desktop trên Ubuntu cần thư viện X11 của hệ điều hành. Ubuntu desktop thường có sẵn; WSL Ubuntu thì thiếu, cài bằng `sudo apt install libice6 libsm6`.

Ảnh chụp từng màn hình do test dựng ra nằm ở `tests\NTech.DataStudio.Trainer.Desktop.Tests\bin\Debug\net10.0\screens\` sau khi chạy test.

Test worker, không cần GPU hay Docker:

```powershell
$env:PYTHONPATH = "trainer\python"
uv run --no-project --python 3.12 --with pytest --with pillow --with pyyaml --with psutil --with numpy --with opencv-python-headless python -m pytest trainer\python\tests -q
```

## Dùng bằng app

```powershell
dotnet run --project src\NTech.DataStudio.Trainer.Desktop
```

| Màn hình | Làm gì |
| --- | --- |
| Máy | Mức đánh giá từng tiêu chí, kèm nguồn số liệu; kiểm tra trong container |
| Dataset | Kiểm tra dataset trong container: lỗi, cảnh báo, số nhãn theo class, kích thước vật thể |
| Cấu hình | Phiên bản và cỡ YOLO, tham số, gợi ý cho wire harness, đánh giá theo cấu hình; train bằng CPU hoặc model thử nghiệm phải tích xác nhận |
| Train | Danh sách run, tiến độ, metric, biểu đồ, tài nguyên, log có lọc; dừng ở cuối epoch và train tiếp |
| Kết quả | Bảng so sánh các run, metric theo class, export và kết quả kiểm chứng ONNX |

Tạo workspace từ `data.yaml` của dataset, hoặc mở workspace đã tạo bằng CLI. App nhớ workspace lần trước. Đóng app không dừng job; mở lại thì app đọc tiếp từ workspace.

## Dùng bằng dòng lệnh

`ntech-trainer` nằm ở `src\NTech.DataStudio.Trainer.Cli\bin\Debug\net10.0\`.

```powershell
ntech-trainer models
ntech-trainer probe --workspace D:\train\wire --model yolo26s --imgsz 1280 --container
ntech-trainer init --workspace D:\train\wire --data D:\exports\wire\data.yaml
ntech-trainer validate --workspace D:\train\wire --imgsz 1280
ntech-trainer train --workspace D:\train\wire --model yolo26s --imgsz 1280 --epochs 100
ntech-trainer export --workspace D:\train\wire --run <id>
ntech-trainer compare --workspace D:\train\wire
```

- Dataset chỉ được tham chiếu và mount chỉ đọc. Workspace không được nằm trong thư mục dataset.
- Máy không có GPU dùng được, hoặc model thử nghiệm, phải thêm `--accept-risk` mới train.
- `--detach` chạy nền. Đóng terminal không dừng job. Xem lại bằng `status` và `follow`.
- `stop` dừng ở cuối epoch hiện tại. `resume` train tiếp sau khi dừng hoặc bị ngắt.
- `--set fliplr=0,hsv_h=0` đổi tham số tăng cường. Gợi ý cho wire harness ở mục 6.3 của thiết kế.

## Thử khi chưa có nhãn thật

`tools/make_smoke_dataset.py` vẽ vật thể giả của 3 class lên ảnh nền. Kết quả chỉ để thử luồng, metric không nói lên chất lượng thật. Dataset ghi `ntech_synthetic: true` nên mọi báo cáo đều ghi rõ.

```powershell
docker run --rm --entrypoint python `
  --mount "type=bind,source=$PWD\trainer\python\tools,target=/tools,readonly" `
  --mount "type=bind,source=C:\Users\ndev\Documents\wire-harness-check\master,target=/bg,readonly" `
  --mount "type=bind,source=D:\train\smoke,target=/out" `
  ntech-trainer:dev-cpu /tools/make_smoke_dataset.py --out /out --backgrounds /bg
```

## Khi đổi phiên bản thư viện

Sửa file `.in` trong `docker/trainer/`, chạy `update-locks.ps1`, build lại cả hai image, rồi chạy lại toàn luồng với mọi phiên bản trong `trainer/catalog/`.
