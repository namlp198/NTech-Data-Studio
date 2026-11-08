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

## Build và đóng gói

Script ở `scripts/trainer/`. Trên Windows dùng `.bat` (gọi `.ps1` cùng tên), trên Linux dùng `.sh`. Hai bên cùng tham số; riêng installer chỉ build được trên Windows.

| Script | Làm gì |
| --- | --- |
| `build` | Publish app và CLI tự chứa vào cùng thư mục `publish\NTechTrainer-<runtime>`, kèm thư mục `image\` để build image trên máy train mà không cần repo |
| `package` | Chỉ Windows: publish win-x64 rồi build installer Inno Setup `dist\NTech-Trainer-Setup-<version>.exe` từ `trainer/installer/NTech-Trainer.iss` |
| `deploy` | Chạy test Trainer, gọi `build`, đóng gói vào `dist\`, ghi `.sha256`; tùy chọn lưu image và chép đi. Trên Windows, win-x64 ra installer, Linux ra `.tar.gz`. `deploy.sh` không có Inno Setup nên win-x64 ra `.zip` |
| `build-image` | Build image worker với tag `ntech-trainer:dev-cpu` hoặc `dev-cuda`. Chạy được từ repo và từ gói đã giải nén |

```powershell
scripts\trainer\deploy.bat                                   # win-x64
scripts\trainer\deploy.bat -Runtime win-x64,linux-x64        # cả hai
scripts\trainer\deploy.bat -SaveImage cuda -Destination \\server\releases\Trainer
scripts\trainer\build-image.bat -Compute cpu
```

```bash
scripts/trainer/deploy.sh                                    # linux-x64
scripts/trainer/deploy.sh --runtime linux-x64,win-x64 --save-image cuda --destination /srv/releases/trainer
scripts/trainer/build-image.sh --compute cuda
```

- Version lấy từ `Directory.Build.props`; `-Version` hoặc `--version` để ghi đè.
- Gói Linux tạo trên Windows vẫn giữ quyền thực thi của `NTechTrainer`, `ntech-trainer` và file `.sh`.
- `-SaveImage` build image rồi `docker save` ra `dist\ntech-trainer-<compute>-<version>.tar.gz`, cho máy train không có internet (`docker load -i <file>`).
- Test Python của worker chỉ chạy khi có `uv` trên PATH; không có thì script cảnh báo rõ là đã bỏ qua.
- Đích đã có file cùng tên mà khác nội dung thì không ghi đè, trừ khi thêm `-Force` hoặc `--force`. Mỗi lần build cho ra hash mới, nên giao lại cùng version thì phải tăng version hoặc dùng `-Force`.

Trên Linux, app và CLI cần `libicu`, app cần thêm thư viện X11. Ubuntu desktop thường có sẵn; bản tối giản hoặc WSL thì phải cài (xem mục dưới).

### Chạy bản Linux trong WSL

Bản Linux không có installer: giải nén `.tar.gz` là chạy. WSLg hiện cửa sổ app, không cần cài gì thêm cho phần hiển thị.

1. Cài thư viện hệ thống. Tên gói ICU theo bản Ubuntu: `libicu78` trên 26.04, `libicu74` trên 24.04 (`apt-cache search --names-only '^libicu[0-9]+$'` để xem).

   ```bash
   sudo apt update && sudo apt install -y libicu78 libice6 libsm6
   ```

2. Giải nén vào ổ Linux (chạy từ ổ Windows qua `/mnt` thì chậm):

   ```bash
   mkdir -p ~/apps && tar -xzf /mnt/d/<repo>/dist/NTechTrainer-<version>-linux-x64.tar.gz -C ~/apps
   ```

3. Mở app, hoặc dùng CLI cùng thư mục:

   ```bash
   ~/apps/NTechTrainer-<version>-linux-x64/NTechTrainer &
   ~/apps/NTechTrainer-<version>-linux-x64/ntech-trainer probe
   ```

- Docker trong WSL dùng chung daemon với Docker Desktop, nên image đã build bên Windows dùng được ngay. Thiếu image thì chạy `image/build-image.sh --compute cpu` (hoặc `cuda`) trong thư mục đã giải nén.
- Workspace tạo trên Windows không mở được trong WSL: tạo workspace mới trên ổ Linux (`~/train/...`). Dataset đọc từ `/mnt/c`, `/mnt/d` được.
- Thiếu `libicu` thì app và CLI thoát ngay với thông báo `Couldn't find a valid ICU package`. Thiếu `libice6`, `libsm6` thì app không mở cửa sổ.
- Cửa sổ app trong WSLg không có cây UI Automation: tự kiểm chỉ làm được bằng ảnh chụp và bấm theo tọa độ.

Ảnh chụp từng màn hình do test dựng ra nằm ở `tests\NTech.DataStudio.Trainer.Desktop.Tests\bin\Debug\net10.0\screens\` sau khi chạy test.

Test worker, không cần GPU hay Docker:

```powershell
$env:PYTHONPATH = "trainer\python"
uv run --no-project --python 3.12 --with pytest --with pillow --with pyyaml --with psutil --with numpy --with opencv-python-headless python -m pytest trainer\python\tests -q
```

## Docker trên Linux có GPU

Train bằng GPU trên Linux cần **Docker Engine** cùng NVIDIA Container Toolkit. **Docker Desktop for Linux không dùng được**: nó chạy container trong một VM, không truyền GPU NVIDIA vào, và giới hạn RAM của VM thường thấp hơn nhiều so với máy (màn hình Máy báo RAM "Weak" và `runtime nvidia: không`). Nhận biết: `docker info --format '{{.Name}} | {{.OperatingSystem}}'` ra `docker-desktop | Docker Desktop`. Máy chỉ train bằng CPU thì Docker Desktop vẫn chạy được, chỉ cần tăng RAM ở Settings → Resources.

Các lệnh dưới đây dành cho Ubuntu 24.04, cần `sudo`, và driver NVIDIA đã có sẵn trên host.

1. Gỡ Docker Desktop và Engine cũ. **Việc này xóa mọi image, container, volume**: `docker save` trước nếu cần giữ.

   ```bash
   systemctl --user stop docker-desktop; sudo apt purge -y docker-desktop
   rm -rf ~/.docker ~/.config/systemd/user/docker-desktop.service
   sudo apt purge -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin docker-ce-rootless-extras docker.io docker-compose docker-doc podman-docker containerd runc
   sudo apt autoremove -y --purge
   sudo rm -rf /var/lib/docker /var/lib/containerd /etc/docker /etc/apt/sources.list.d/docker.* /etc/apt/keyrings/docker.asc
   ```

   Xóa `~/.docker` là cần thiết: `config.json` của Desktop có thể còn trỏ credential helper `desktop`, làm Engine mới lỗi khi pull. `apt purge` báo vài gói "không được cài" là bình thường.

2. Cài Docker Engine từ kho của Docker, rồi cho user dùng docker không cần sudo (mở shell mới để nhóm có hiệu lực):

   ```bash
   sudo apt update && sudo apt install -y ca-certificates curl && sudo install -m 0755 -d /etc/apt/keyrings
   sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc && sudo chmod a+r /etc/apt/keyrings/docker.asc
   echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
   sudo apt update && sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
   sudo usermod -aG docker $USER && newgrp docker
   ```

3. Cài NVIDIA Container Toolkit và đăng ký runtime:

   ```bash
   curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
   curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
   sudo apt update && sudo apt install -y nvidia-container-toolkit
   sudo nvidia-ctk runtime configure --runtime=docker && sudo systemctl restart docker
   ```

4. Kiểm tra:

   ```bash
   docker info --format '{{.Name}} | {{.OperatingSystem}} | {{.MemTotal}} | {{.Runtimes}}'
   docker context ls
   docker run --rm --gpus all ubuntu nvidia-smi
   ```

   Kết quả đúng: tên host thật (không có "Docker Desktop"), `MemTotal` bằng RAM máy, `nvidia` nằm trong danh sách runtime, chỉ còn context `default` (còn `desktop-linux` thì `docker context rm desktop-linux`), và `nvidia-smi` trong container in ra bảng GPU.

5. Build lại image worker, vì Engine mới có kho image trống: `image/build-image.sh --compute cuda`. Rồi mở Trainer, bấm "Đọc lại" ở màn hình Máy và "Kiểm tra trong container".

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
