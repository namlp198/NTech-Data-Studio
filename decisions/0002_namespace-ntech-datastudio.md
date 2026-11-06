# 0002: Namespace NTech.DataStudio

Ngày: 2026-11-06

**Quyết định:** toàn bộ namespace, assembly và project dùng `NTech.DataStudio.*`, kể cả Trainer sau này (`NTech.DataStudio.Trainer`). Không dùng `NTech.NVision.*`. Tên hiển thị là NTech Data Studio.

**Thay đổi so với tài liệu thiết kế gốc:** bản gốc dùng `NTech.VisionStudio.*`. Chủ dự án yêu cầu đổi sang `NTech.DataStudio` khi bắt đầu triển khai, trước lần commit đầu tiên, nên không có code hay project nào đã phát hành dùng tên cũ.

**Hệ quả:** tên code khớp với tên sản phẩm. Bản gốc của tài liệu thiết kế vẫn ghi tên cũ; bản trong repo (`docs/DESIGN_DOC.md`) là bản đúng.
