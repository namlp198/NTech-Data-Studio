# 0003: NVision chỉ nhận file .nvmodel

Ngày: 2026-11-06

**Quyết định:** NVision là sản phẩm riêng. NVision chỉ nhận file `.nvmodel` do Trainer xuất ra; không đọc dataset và không gọi trực tiếp vào Data Studio.

**Hệ quả:** đặc tả `.nvmodel` (`docs/formats/NVMODEL.md`) sẽ viết khi làm Trainer. MVP không có phụ thuộc nào tới NVision.
