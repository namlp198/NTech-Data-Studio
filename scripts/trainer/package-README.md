# NTech Trainer

Train YOLO detect trong Docker và xuất ONNX đã kiểm chứng.

| File | Là gì |
| --- | --- |
| `NTechTrainer` (`.exe` trên Windows) | App Desktop |
| `ntech-trainer` (`.exe` trên Windows) | Dòng lệnh, cùng phần lõi và cùng workspace với app |
| `catalog/` | Danh mục phiên bản YOLO |
| `image/` | Mã nguồn image worker và script build image |

Bản này tự chứa .NET, không cần cài runtime.

## 1. Cài Docker

- Windows: Docker Desktop.
- Linux: Docker Engine. Máy có GPU NVIDIA thì cài thêm NVIDIA Container Toolkit.
- Không dùng Docker Desktop for Linux để train bằng GPU: nó không truyền GPU vào container và giới hạn RAM của VM. Dùng Docker Engine (xem mục "Docker trên Linux có GPU" trong `trainer/README.md` của repo).

## 2. Có image worker

App và CLI tìm image tên `ntech-trainer:dev-cpu` hoặc `ntech-trainer:dev-cuda`. Chọn một trong hai cách.

Build trên máy này (cần internet):

```bash
image/build-image.sh --compute cuda      # Linux
image\build-image.bat -Compute cuda      # Windows
```

Hoặc nạp file image được giao kèm:

```bash
docker load -i ntech-trainer-cuda-<version>.tar.gz
```

Image `cuda` chỉ dùng được với GPU NVIDIA từ Turing (SM 7.5) trở lên. Không có GPU thì dùng `cpu`.

## 3. Chạy

```bash
./NTechTrainer                           # app
./ntech-trainer models                   # CLI
```

Trên Linux, app và CLI cần `libicu`; app cần thêm thư viện X11. Ubuntu desktop thường có sẵn. Bản cài tối giản hoặc WSL Ubuntu thì cài, với tên gói ICU theo bản Ubuntu (`libicu78` trên 26.04, `libicu74` trên 24.04):

```bash
sudo apt install -y libicu78 libice6 libsm6
```

Workspace không chuyển được giữa Windows và Linux: tạo workspace mới trên máy train.

## Giấy phép

Model train bằng gói `ultralytics` thuộc AGPL-3.0 kể cả dùng nội bộ theo lập trường của Ultralytics, trừ khi có Enterprise License. Cần kết luận trước khi đưa model vào sản xuất hoặc bán.
