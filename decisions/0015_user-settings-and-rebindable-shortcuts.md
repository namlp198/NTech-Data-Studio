# 0015. Cài đặt theo người dùng, phím tắt của Annotate đổi được

Ngày: 2026-11-08

## Bối cảnh

Phím tắt của Annotate nằm cứng trong `AnnotateViewModel.HandleKey`. Người dùng cần đổi phím theo thói quen, và có lý do thật: bộ gõ Telex làm hỏng nhiều phím chữ đơn (xem CLAUDE.md), bàn phím mỗi máy một khác. App cũng chưa có chỗ nào lưu cấu hình của người dùng.

## Quyết định

1. **Cài đặt là của người dùng, không của project.** Lưu ở `%APPDATA%\NTech\DataStudio\settings.json`. Project vẫn tự chứa (chép sang máy khác mở được ngay) và không mang phím tắt của ai theo.
2. **Chỉ lưu phần người dùng đã đổi.** File ghi `{ "Version": 1, "Shortcuts": { "BoxTool": ["X"] } }`. Hành động thêm ở phiên bản sau tự có phím mặc định, và đổi mặc định trong code đến được người chưa từng đổi phím đó.
3. **Phím là chuỗi** (`Ctrl+Shift+Z`, tên theo `System.Windows.Input.Key`, riêng Enter, PageUp, PageDown, CapsLock được gộp tên). `Shortcut` ở Application không dùng kiểu WPF nên test được không cần cửa sổ; Desktop đổi `Key` sang tên ở `KeyNames`. Tên hành động trong `ShortcutAction` được lưu vào file: không đổi tên.
4. **Một phím chỉ làm một việc.** Gán phím đang dùng cho việc khác bị từ chối kèm tên việc đó (không tự cướp, không tự hoán đổi; đổi chỗ hai phím làm trong ba bước). Khi đọc file mà hai hành động trùng phím (sửa tay, hay file cũ), hành động sau về mặc định, nên không bao giờ có phím làm hai việc.
5. **Phím cố định:** 1 đến 9 (chọn class, theo thứ tự class) và Alt (Windows dùng cho menu). Esc dùng để hủy khi đang gán, nên không gán được cho hành động khác (hành động Esc vẫn đổi sang phím khác được).
5a. **Space gán được, chạy khi nhả.** Giữ Space rồi kéo chuột là cách pan, nên một Space đã gán chỉ chạy khi nhả phím và chỉ khi trong lúc giữ không có cú kéo pan nào (`AnnotationCanvas.SpaceUsedForPan`, xử lý ở `AnnotateView`). Chưa gán thì Space chỉ làm việc pan như cũ. Giữ phím không lặp hành động. Các phím khác vẫn chạy ngay khi nhấn.
6. **Mỗi hành động có thể có nhiều phím mặc định** (V và S cùng chọn công cụ Chọn; A và ← cùng là ảnh trước). Bấm Đổi phím thay mọi phím của hành động bằng một phím; Mặc định trả lại đủ phím gốc.
7. **Shift được bỏ qua khi chỉ có phím không Shift được gán**, để `+` hoạt động trên mọi bố cục bàn phím (là Shift+`=` ở bàn phím Mỹ). Tổ hợp có Ctrl thì khớp chính xác.
8. **Lỗi không chặn việc dùng app.** Không đọc được file thì dùng mặc định và báo trên màn hình Cài đặt; không ghi được thì thay đổi vẫn có hiệu lực trong lần chạy này và màn hình nói nó sẽ mất khi mở lại. Ghi vào file tạm rồi đổi tên, nên mất điện giữa chừng không để lại nửa file.
9. **Màn hình Cài đặt** là tab riêng trên thanh trên cùng. Hiện có một mục là Phím tắt; mục khác thêm sau trong cùng màn hình.

## Không làm (cố ý)

- Không cho đổi phím 1 đến 9 và chuột (kéo, cuộn, chuột giữa).
- Không gán nhiều phím cho một hành động từ giao diện (file có thể chứa nhiều phím và vẫn đọc được); không nhập/xuất cấu hình.
- Không có phím tắt toàn app (đổi tab). Hiện chỉ Annotate có phím tắt.
- Không tự sửa phím khi Telex đang bật; màn hình chỉ cảnh báo bằng chữ.

## Hệ quả

- Thêm một hành động phím tắt mới: thêm vào `ShortcutAction` và `ShortcutCatalog` (kèm phím mặc định không trùng, có test kiểm), rồi xử lý ở `AnnotateViewModel.Run`.
- Tooltip trên thanh công cụ đọc từ `ShortcutHints` nên tự theo phím đang dùng. Đừng viết cứng tên phím vào tooltip hay tài liệu cho người dùng.
