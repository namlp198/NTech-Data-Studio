# 0019: Project có nhiều task mục tiêu, gán tự động theo task, Cài đặt chia mục

Ngày: 2026-11-11

Quyết định 0014 cho project một task mục tiêu duy nhất. Dùng thật cho thấy một project cần cả box (Detect) lẫn polygon (Segment), và gán tự động (0017, 0018) mới chỉ ra polygon.

**Quyết định:**

1. **Project có một tập task** (0, 1 hoặc nhiều trong Detect, Segment, OBB, Classify). Khóa `target_tasks` trong `project_meta` giữ các khóa nối bằng dấu phẩy, theo thứ tự của `TargetTasks.All`. Không thêm migration. Khóa cũ `target_task` vẫn được ghi (task đầu tiên) để bản app cũ còn thấy một task, và project chỉ có khóa cũ đọc thành tập một task. Tập rỗng là "chưa chọn": mọi công cụ vẽ dùng được như trước.
2. **Công cụ vẽ bật khi bất kỳ task nào của project cần nó** (`TaskTools.Allows(tasks, tool)`). Detect + Segment bật Box, Polygon và Magic polygon. Tooltip công cụ tắt kể đủ các task.
3. **Export** cảnh báo (không chặn) khi task của profile không nằm trong tập task của project, và chọn sẵn profile đầu tiên có task thuộc tập. `version.json` có thêm `targetTasks` (mảng khóa); `targetTask` giữ lại, là task đầu tiên. Xuất Segment từ ảnh chỉ có box vẫn là lỗi như trước (0014): không bỏ ảnh âm thầm.
4. **Gán tự động theo task.** Detect: model detect cho ra **box** (`BoxAutoLabeler`, không qua SAM). Segment: như 0018 (model segment, hoặc detect + SAM). `ConfiguredAutoLabeler.For(task)` chọn bộ gán; `AutoLabelService` nhận task. Project có nhiều task tự gán được (Detect và Segment trong tập task của nó; project không nhắm cái nào thì được mời cả hai) thì Annotate hiện ô "Gán tự động theo task" cạnh các nút Gán tự động; project một task không có ô này.
5. **"Xóa hết, gán mới" chỉ xóa nhãn cùng loại hình** với thứ sắp gán (gán polygon xóa polygon, gán box xóa box), không xóa nhãn khóa. Nếu không, gán lại polygon sẽ làm mất box của project Detect + Segment. Hộp hỏi nói rõ "box" hay "polygon".
6. **Mỗi task gán tự động có ngưỡng tin cậy riêng** (`AutoLabelMinScore` cho Segment, `AutoLabelDetectMinScore` mới cho Detect, mặc định 0,4). Model detect `AutoLabelDetectModel` dùng chung giữa task Detect và cách detect + SAM của Segment (chọn một lần ở thẻ Detect); model segment riêng. Công tắc nâng cao (0018) vẫn là một cho cả hai.
7. **Cài đặt chia mục** (danh sách mục bên trái, nội dung bên phải): Gán nhãn tự động (công tắc, thẻ Detect, thẻ Segment, mỗi thẻ có huy hiệu trạng thái, model, trạng thái, ngưỡng), Magic polygon (SAM), Phím tắt. Style mới ở `Themes/Components/Settings.xaml` (`SettingsNav.Item`, `Settings.Badge`, `Settings.Row`); chọn mục qua `IsChecked`, không qua Command.

**Hệ quả:**

- Test: tập task (lưu, đọc lại, khóa cũ, thứ tự), công cụ theo nhiều task, cảnh báo export, `version.json`, gán Detect ra box, thay thế theo loại hình, ngưỡng theo task. Chạy app thật qua UI Automation: tích hai task ở Project, Annotate bật cả Box và Polygon, ô chọn task hiện, nút Gán tự động báo thiếu model cho từng task, Cài đặt chia mục đúng.
- **Chưa chạy với model detect thật**: máy này không có file ONNX detect của Trainer; gán Detect mới được thử đến chỗ báo thiếu model và bằng test dữ liệu giả.
- Nhãn box và polygon của một ảnh sống chung; ảnh có cả hai đi qua Export như trước (box → polygon 4 đỉnh, polygon → box bao quanh, kèm cảnh báo).
- Một lỗi tìm được nhờ chạy thật: `ColumnDefinition.Width` đòi `GridLength`, token `Layout.SettingsLabelWidth` ban đầu là `Double` và làm app thoát khi mở Cài đặt (build vẫn xanh, đúng như bẫy "thiếu token chỉ lỗi lúc chạy").
- Số thứ tự 0019 theo Data Studio; nhánh Trainer có thể đã dùng số này.
