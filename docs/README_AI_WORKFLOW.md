# Quy trình làm việc với AI

1. Đọc theo thứ tự trong `CLAUDE.md`.
2. Làm theo task trong `tasks/active.md`; việc chưa tới lượt nằm ở `tasks/backlog.md`.
3. Mỗi bước kết thúc bằng một bản chạy được.
4. Quyết định kiến trúc mới thì thêm một file vào `decisions/` và một dòng vào `decisions/0000_INDEX.md`.
5. Cuối phiên cập nhật file bàn giao của phần đang làm: đã làm gì, đã kiểm tra gì, còn lại gì.

## Phát triển song song Data Studio và Trainer

Hai tool ở chung repo vì phát hành chung trong một sản phẩm. Trainer không tham chiếu project nào của Data Studio; hai bên chỉ gặp nhau ở định dạng dataset xuất ra.

| | Data Studio | Trainer |
| --- | --- | --- |
| Nhánh | `dev/data-studio` | `dev/trainer` |
| Code | `src/` còn lại, `profiles/`, `installer/` | `src/NTech.DataStudio.Trainer*`, `trainer/`, `docker/trainer/`, `docs/trainer/` |
| Bàn giao | `handoff/latest.md` | `handoff/trainer.md` |
| Task | Mục Data Studio trong `tasks/active.md` | Mục Trainer trong `tasks/active.md` |

**Quy tắc:**

1. Khi hai bên cùng đang có việc, mỗi bên làm trên một git worktree và nhánh riêng. Không làm song song trên cùng một thư mục, vì thay đổi của hai bên sẽ lẫn nhau.
2. Commit chỉ gồm file của phần mình: `git add <đường dẫn>`, không dùng `git add -A` hay `git add .` khi còn thay đổi của phiên khác.
3. `main` chỉ nhận bản đã build và test xanh. Merge sớm và thường xuyên, không để nhánh lệch xa.
4. File dùng chung (`Directory.Build.props`, `Directory.Packages.props`, `global.json`, `NTech.DataStudio.slnx`, `scripts/`) đổi trong commit riêng, chạy test của cả hai bên trước khi merge.
5. Số quyết định trong `decisions/` có thể trùng giữa hai nhánh. Số chỉ chốt khi merge vào `main`: thấy trùng thì đổi số của bên merge sau.
6. Đổi định dạng dataset xuất ra thì phải chạy test đọc bản xuất mẫu của Trainer, và sửa cả hai phía trong cùng một lần merge.

Đọc file bàn giao và mục task của phần đang làm; chỉ đọc phần kia khi việc có liên quan.
