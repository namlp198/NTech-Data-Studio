# Cài Docker trên Linux để train

Áp dụng cho Ubuntu 24.04 (các bản Ubuntu khác dùng cùng kho `download.docker.com`, chỉ đổi codename). Mọi lệnh cần `sudo`. Máy có GPU NVIDIA thì driver phải có sẵn trên host (`nvidia-smi` chạy được).

## Chọn Docker nào

| | Docker Engine | Docker Desktop for Linux |
| --- | --- | --- |
| Dùng được với Trainer | Có | Chỉ train bằng CPU |
| GPU NVIDIA trong container | Có, với NVIDIA Container Toolkit | Không: container chạy trong VM, không nhận GPU |
| RAM cho container | Toàn bộ RAM máy | Giới hạn của VM (mặc định thấp, ví dụ 7,7 GB trên máy 60,5 GB) |

Train bằng GPU thì dùng **Docker Engine**. Màn hình Máy của Trainer báo RAM "Weak" và `runtime nvidia: không` là dấu hiệu đang dùng Docker Desktop. Kiểm tra:

```bash
docker info --format '{{.Name}} | {{.OperatingSystem}} | {{.MemTotal}}'
```

Kết quả `docker-desktop | Docker Desktop | ...` nghĩa là Docker Desktop.

## 1. Gỡ Docker đã cài (bỏ qua nếu máy chưa có Docker)

**Bước này xóa mọi image, container, volume.** Cần giữ gì thì `docker save` hoặc sao lưu trước.

Docker Desktop:

```bash
systemctl --user stop docker-desktop; sudo apt purge -y docker-desktop
rm -rf ~/.docker ~/.config/systemd/user/docker-desktop.service
```

Xóa `~/.docker` là cần thiết: `config.json` của Desktop có thể còn trỏ credential helper `desktop`, làm Engine mới lỗi khi pull.

Docker Engine, các bản đóng gói khác và gói xung đột:

```bash
sudo apt purge -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin docker-ce-rootless-extras docker.io docker-compose docker-doc podman-docker containerd runc
sudo apt autoremove -y --purge
sudo rm -rf /var/lib/docker /var/lib/containerd /etc/docker /etc/apt/sources.list.d/docker.* /etc/apt/keyrings/docker.asc
```

`apt purge` báo vài gói "không được cài" là bình thường.

## 2. Cài Docker Engine

```bash
sudo apt update && sudo apt install -y ca-certificates curl && sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc && sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo $VERSION_CODENAME) stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update && sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

Cho user hiện tại dùng docker không cần sudo, rồi mở shell mới (hoặc đăng xuất, đăng nhập lại) để nhóm có hiệu lực:

```bash
sudo usermod -aG docker $USER && newgrp docker
```

## 3. Cài NVIDIA Container Toolkit (chỉ khi train bằng GPU)

```bash
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
sudo apt update && sudo apt install -y nvidia-container-toolkit
sudo nvidia-ctk runtime configure --runtime=docker && sudo systemctl restart docker
```

## 4. Kiểm tra

```bash
docker info --format '{{.Name}} | {{.OperatingSystem}} | {{.MemTotal}} | {{.Runtimes}}'
docker context ls
docker run --rm --gpus all ubuntu nvidia-smi
```

Kết quả đúng:

- Tên host thật của máy, không có "Docker Desktop".
- `MemTotal` bằng RAM máy.
- `nvidia` nằm trong danh sách runtime.
- Chỉ còn context `default`. Còn `desktop-linux` thì chạy `docker context rm desktop-linux`.
- `nvidia-smi` trong container in ra bảng GPU.

## 5. Build lại image worker

Engine mới có kho image trống. Chạy `image/build-image.sh --compute cuda` (hoặc `cpu`) trong thư mục đã giải nén của Trainer, hoặc `docker load -i` file image được giao kèm. Rồi mở Trainer, bấm "Đọc lại" ở màn hình Máy và "Kiểm tra trong container".
