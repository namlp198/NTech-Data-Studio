# 0001: .NET 10, WPF, MVVM

Ngày: 2026-11-06

**Quyết định:** MVP viết hoàn toàn bằng .NET 10 (LTS), WPF và MVVM (`CommunityToolkit.Mvvm`). Python chỉ vào dự án ở giai đoạn Trainer, chạy thành process riêng.

**Lý do:**

- Cả 4 module đều là xử lý ảnh và ghi file, không có bước train.
- Dùng lại được pattern Geo Measure, design token và cấu trúc MVVM của robo-station.
- Không phải đóng gói Python và torch, nên bộ cài nhẹ hơn nhiều.
- Có thể obfuscate và gắn license key khi bán thương mại.

**Hệ quả:** thư viện xử lý ảnh là OpenCvSharp4 (Apache 2.0). Không dùng ImageSharp vì Six Labors Split License.
