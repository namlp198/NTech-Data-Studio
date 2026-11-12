# Bàn giao

Ngày: 2026-11-07

Làm trên nhánh `dev/data-studio`, worktree `.claude/worktrees/data-studio` (từ 2026-11-08). Thư mục gốc của repo giữ `main`.

## Cập nhật 2026-11-09: chọn ảnh vào dataset (Annotate)

- Migration 2: cột `images.in_dataset` (mặc định 0), view `active_images` dựng lại. Khóa `dataset_selection_only` trong `project_meta`.
- Annotate: ô "Chỉ đưa ảnh được chọn vào dataset" ở dải ảnh; khi bật, mỗi thumbnail có ô chọn. Tắt thì dataset lấy mọi ảnh đã gán nhãn như trước. Ảnh chưa chọn bị `VersionBuilder` bỏ (`DatasetImage.Included`), màn Dataset ghi số ảnh bị loại. Việc chọn không đi qua `AnnotateService.Execute` (không phải sửa nhãn, không có undo).
- Test: 356/356 ở Application (thêm 1; `Open_TwiceDoesNotReapplyMigrations` đổi sang version 2). App khởi động được; **chưa bấm thử** ô chọn trong Annotate và màn Dataset trên app thật. Chưa có phím tắt cho việc chọn ảnh.

## Cập nhật 2026-11-09 (2): gán nhãn tự động bằng model segment (quyết định 0017)

- `IAutoLabeler`/`AutoLabelService` (Application), `OnnxAutoLabeler` + `YoloSegDecoder` (Imaging) đọc ONNX segment theo hợp đồng `model.meta.json` của Trainer. Cài đặt có mục "Model gán nhãn tự động" (chọn .onnx, ngưỡng 5 đến 95%, mặc định 40%). Annotate có nút "Gán tự động" cho ảnh đang mở: thêm polygon `source=model` thành một lần Undo, ảnh ở "Cần review".
- Test: 360 Application, 101 Imaging (giải mã bằng tensor tổng hợp: hoàn tác letterbox, cắt theo box, ngưỡng, clip). Build 0 cảnh báo.
- **Đã chạy với model thật** (YOLO26-seg 3 class `dau_cos`, `bang_dinh`, `day_thit`, 640 px, Ultralytics export thô không có `model.meta.json`) trên một ảnh wire harness 1024x576: 30+ polygon, vẽ lên ảnh thấy bám đúng đầu cos và băng dính, tọa độ đúng sau khi hoàn tác letterbox. Lỗi tìm được nhờ chạy thật: `Mat.GetArray<byte>` từ chối Mat 3 kênh (dùng `Vec3b`). Test `OnnxAutoLabelerModelTests` chạy khi đặt `NTECH_AUTOLABEL_MODEL` và `NTECH_AUTOLABEL_IMAGE` (xem chú thích đầu file); tự dựng meta khi thiếu. Một vài polygon của dây dài bị nối thành vùng lớn. Chưa bấm thử nút và màn Cài đặt trên app thật; model Ultralytics thô vẫn cần `model.meta.json` cạnh nó để app nhận.
- Chưa làm: gán hàng loạt, phím duyệt-sang-ảnh-kế, màu riêng cho nhãn tự động, bộ lọc nhãn tay, chặn nhãn chưa duyệt khi export.

## Cập nhật 2026-11-10: gán tự động hàng loạt và chấm màu riêng cho ảnh có nhãn model

- Nút "Gán tự động tất cả" (và "Dừng gán" khi đang chạy) ở dải ảnh: gán cho mọi ảnh trạng thái New, tuần tự, có tiến độ "n / tổng". Ảnh đã có nhãn mà chưa tích chọn vào dataset thì hỏi **một lần** cho cả nhóm với 3 nút giống gán một ảnh (Hủy lệnh, Ghi đè = giữ nhãn cũ thêm nhãn model, Xóa hết gán mới); ảnh đã tích chọn thì giữ nguyên; ảnh Null bị bỏ qua. Mỗi ảnh một lần sửa nhãn (Undo được). Ảnh lỗi hay không thấy vật thể thì đếm và báo cuối lượt, không dừng cả lượt. Rời project thì lượt tự hủy.
- Chấm tím (`Brush.ImageStatus.AutoLabeled`) ở góc ảnh khi có nhãn `source=model` và chưa Duyệt; Duyệt xong về chấm xanh. Cờ lấy từ `AnnotateImage.HasModelLabels` (bảng annotations).
- Chọn "hỏi một lần cho cả nhóm" là cách tôi hiểu "gặp file đã gán"; hỏi từng ảnh thì phải bấm hàng chục lần.
- Test 366 Application (thêm 1 cho cờ nhãn model). Build 0 cảnh báo. **Chưa chạy giao diện**: chưa bấm thử nút hàng loạt, hộp hỏi, nút Dừng và màu chấm trên app thật.

## Cập nhật 2026-11-09 (3): hộp thoại riêng, tên class có `_`, gán tự động 3 lựa chọn, chọn nhiều nhãn

- `MessageBox` hệ thống đã bị thay hết (4 chỗ hỏi và 1 chỗ báo lỗi chưa bắt ở `App.xaml.cs`) bằng `DialogPrompt` + `Views/Dialogs/AppDialogWindow` dựng theo `AppDialog` của RoboStation (đọc, không sửa robo-station): thanh màu và ô huy hiệu theo mức độ, tối đa 3 nút, mục "Chi tiết" cho lỗi. `IUserPrompt` giờ có `Confirm(title, message, yesLabel, danger)`, `Choose(...)`, `Inform(...)`. Cỡ nằm ở token `Layout.Dialog*` (Sizing.xaml). Huy hiệu vuông, không bo tròn.
- Menu chuột phải: `AppContextMenu.NewItem` đổi `_` thành `__` (WPF coi `_` trong Header là phím tắt nên `dau_cos` hiện `daucos`).
- Gán tự động khi ảnh đã có nhãn: hộp 3 nút "Hủy lệnh", "Ghi đè" (giữ nhãn cũ, thêm nhãn model lên trên), "Xóa hết, gán mới" (xóa nhãn không khóa rồi gán, cùng một lần Undo). "Ghi đè" là cách hiểu của tôi; nếu ý bạn là thay nhãn cũ chỗ trùng thì cần đổi.
- Chọn nhiều nhãn: Ctrl+bấm trên ảnh, Ctrl/Shift+bấm trong danh sách, nút "Chọn tất cả", nút "Xóa N nhãn đã chọn" và phím Delete xóa cả loạt trong một lần sửa (Undo gỡ cả loạt; nhãn khóa được giữ). Đổi class cũng áp cho mọi nhãn đã chọn. Khi chọn nhiều thì không sửa đỉnh.
- Test: 365 Application (thêm 5 cho chọn nhiều, xóa nhiều, đổi class nhiều, gán tự động ghi đè và thay). Build 0 cảnh báo.
- **Chưa chạy giao diện**: không thấy hộp thoại, danh sách nhãn chọn nhiều và menu bằng mắt. Lúc kiểm có app bản cài đang mở trên cùng project của bạn nên tôi không mở bản thứ hai.

## Đã làm

- Module 4 Export: kiểm tra trước khi xuất (`DatasetValidator`), xuất YOLO26 detect, segment, OBB, classify và COCO detect, segment (`ExportService` + `NTech.DataStudio.Exporters`), `README.dataset.md` và `version.json` trong mỗi bản xuất, tùy chọn zip, màn hình Export. Xem `docs/design/screens/EXPORT.md`.
- Quyết định 0008: các lựa chọn khi ghi file (không có `path:` trong `data.yaml`, OBB co về tâm cho vừa ảnh, nhãn không biểu diễn được thì bỏ và báo, id class liên tục từ 0 và COCO từ 1, ảnh không chỉnh thì chép nguyên master, tên file ASCII, ghi vào `.partial` rồi đổi tên).
- Ảnh và nhãn của mỗi bản xuất đọc cùng `RenderPlan` với preview ở Dataset (`RenderPlanner` + `AugmentSampler` + `OpenCvAugmentationEngine` + `LabelTransformer`). Hình nhãn do `LabelShapes` quyết định cho mọi định dạng.
- Bộ đọc nhãn `YoloLabelParser` và `CocoLabelParser` (dùng cho test vòng đọc lại). Chưa nối vào project.
- Exporters trước đó rỗng; giờ có code và project test.

## Đã kiểm tra

- `dotnet build`: 0 cảnh báo, 0 lỗi. `dotnet test`: 486/486 (Domain 134, Imaging 72, Exporters 23, Application và Storage 257). Trước đó là 400.
- `ExportService` chạy trên project thật (thư mục tiếng Việt, ảnh master JPEG thật, engine OpenCV): đúng cấu trúc thư mục; ảnh không chỉnh được chép nguyên byte; nhân ×3 chỉ ở train; nhãn bám khối trắng trong ảnh sau lật, xoay, scale, dịch (tâm lệch dưới 3 px, kiểm tra 20+ ảnh); cùng một version xuất hai lần ra cùng từng byte; hủy giữa chừng hoặc một master hỏng không để lại thư mục nào; không ghi đè thư mục lạ; zip; COCO; phân loại; class bị bỏ không để lỗ hổng id.
- Đột biến: cho `ExportService` tính nhãn bằng `RenderPlan` không tăng cường thì đúng một test (căn nhãn) thất bại; khôi phục thì qua.
- Chạy app thật qua UI Automation trên 48 ảnh tổng hợp: chưa project, chưa version, xuất YOLO detect, lần xuất thứ hai bị từ chối rồi qua khi bật ghi đè, Segment bị chặn (ảnh chỉ có box), phân loại bị chặn (chưa có tag), COCO kèm zip, imgsz "abc" bị báo lỗi. Đọc file trên đĩa: 69 ảnh train, 69 file nhãn, 138 dòng nhãn khớp README, mọi tọa độ trong 0..1. Vẽ nhãn từ file `.txt` lên ảnh đã xuất: khung bám đúng khối màu kể cả bản xoay và dịch.
- Lỗi thật tìm được khi chạy app: lần vào Export đầu tiên không kiểm tra gì và nút Xuất bị khóa (kiểm tra bị bỏ qua vì `IsLoading` còn true). Đã sửa; chạy lại thì có kết quả ngay.

## Chưa kiểm tra

- **Chưa chạy `yolo train` hay RF-DETR bằng bản xuất.** Các giả định chưa kiểm chứng: Ultralytics đọc `data.yaml` không có `path:` (lấy thư mục chứa file làm gốc) và từ chối ảnh có tọa độ OBB ngoài 0..1; RF-DETR đọc category COCO bắt đầu từ 1 không có category giữ chỗ; tên model và lệnh mẫu trong README (`yolo26s-seg.pt`, `-obb`, `-cls`, lệnh RF-DETR). Python và các thư viện này không được dùng trong repo (AGPL).
- Ảnh thật, bộ vài trăm ảnh thật. Chưa đo xuất vài nghìn ảnh 20 MP (RAM, thời gian). Số luồng tính theo ảnh lớn nhất của version.
- Nhãn polygon, rotated box và tag trên app thật: bản chạy app chỉ có box. Các đường này được kiểm bằng test (Segment, OBB, phân loại, kể cả OBB nhô ra ngoài), chưa bằng một bản xuất thật.
- `ExportViewModel` và view không có test tự động riêng. Phím tắt, thứ tự Tab của form. Nút Mở thư mục và hộp chọn thư mục (`Chọn thư mục...`) chưa bấm thử. Hủy khi đang xuất qua nút Hủy chưa bấm thử trong app (đã kiểm bằng test ở tầng service).
- Chưa chạy lại installer sau thay đổi này (không đổi script hay cách publish; Exporters vốn đã được Desktop tham chiếu).
- Nhập nhãn có sẵn vào project: chỉ có bộ đọc, chưa có service ghép ảnh và chưa có màn hình.

## Lưu ý

- Bộ đếm "nhãn dưới 50" trong cảnh báo tính trên nhãn gốc của version, còn bảng trong README tính trên bản xuất đã nhân ×N; hai số khác nhau là bình thường.
- Test nhiều tập cần mỗi ảnh một `source_image_id` riêng (ảnh cùng nguồn luôn cùng tập); `AnnotateFixture` mặc định cho toàn train. Xem CLAUDE.md.
- Tập tin kiểm thử trong scratchpad (`seed`, `smoke_export.ps1`) không nằm trong repo.

## Tiếp theo

Xem `tasks/active.md`. Chưa push, repo không có remote.

Bàn giao của NTech Trainer nằm ở `handoff/trainer.md`. File này chỉ dành cho Data Studio.

## Phiên sau Module 4: bốn yêu cầu bổ sung (2026-11-07)

- **Pan khi zoom:** khi ảnh đã zoom lớn hơn vừa khung, giữ chuột trái trên chỗ trống rồi kéo thì pan (công cụ Chọn và Tag). Bấm vào nhãn thì vẫn chọn và kéo nhãn; công cụ vẽ vẫn dùng Space hoặc chuột giữa để pan. Cuộn chuột vẫn zoom. `AnnotationEditor.IsOverLabel` quyết định có bấm trúng nhãn không (3 test).
- **Làm sạch:** chỉ "Trùng hoàn toàn (SHA-256)" bật sẵn; gần trùng, mờ, quá tối, cháy sáng tắt. Chỉ đổi mặc định ở màn hình; `CleanOptions` ở Application giữ mặc định cũ.
- **Phím tắt đổi công cụ:** Q (công cụ kế) và Shift+Q (công cụ trước), S như V (Chọn), + và - để zoom. `AnnotationEditor.CycleTool` (2 test).
- **Segmentation:** vốn đã có (công cụ Polygon, YOLO segment, COCO segment, profile `yolo26-segment` và `rf-detr-segment`). Đã thêm profile `maskrcnn-segment` (COCO segment), kèm ghi chú train riêng trong README bản xuất, và mục Segmentation ở `EXPORT.md`. Chưa có mặt nạ điểm ảnh (PNG), RLE hay segmentation ngữ nghĩa.
- Test: 492 (Domain 134, Imaging 72, Exporters 23, Application và Storage 263). Build 0 cảnh báo.
- **Chạy app thật:** mặc định làm sạch đọc lại đúng (chỉ SHA-256 bật); `maskrcnn-segment` có trong danh sách profile; kéo chuột trái khi đã zoom làm ảnh dịch đúng, công cụ Box không pan; Q và Shift+Q đi hết 5 công cụ hai chiều.
- **Lỗi tìm được khi chạy app:** máy kiểm có bộ gõ tiếng Việt Telex bật, nên gõ `e` lặp thành `ê` làm app nhận Backspace và bỏ mất phím. Vì vậy dùng Q thay E. Phím chữ đơn có sẵn (A, D, S, R...) vẫn có thể bị Telex làm hỏng khi bật tiếng Việt; tắt IME ở WPF không chặn được. Ghi ở `ANNOTATE.md` và `CLAUDE.md`. Cần cân nhắc phím tắt không phải chữ nếu người dùng thường gõ tiếng Việt khi gán nhãn.
- **Chưa kiểm tra:** pan khi kéo trên polygon lõm hoặc khi nhiều nhãn chồng nhau (chỉ kiểm tra vùng trống); pan trên màn hình cảm ứng; `maskrcnn-segment` chưa train thật (imgsz 800 và stride 32 là giả định).

## Phiên tiếp: pan mọi trạng thái, công cụ Pan, tab thu gọn, Esc (2026-11-07)

- **Pan ở mọi mức zoom:** kéo chuột trái trên chỗ trống (công cụ Chọn, Tag) dịch ảnh kể cả khi ảnh đang vừa khung; không còn điều kiện phải zoom. Thêm công cụ Pan (`AnnotateTool.Pan`, phím G) ở thanh công cụ, ngay trên nút Phóng to; kéo ở bất kỳ chỗ nào, kể cả trên nhãn. Con trỏ thành bàn tay khi ở công cụ Pan. Q và Shift+Q giờ quay vòng qua cả Pan (6 công cụ).
- **Nút thu gọn:** panel Class và dải ảnh có nút tròn (hai mũi tên lồng nhau) đè lên left-center của panel và top-center của dải; bấm thu gọn hoặc mở lại (`Button.CircleTab`, icon `Icon.ChevronDouble*`). Chỉ lưu trong bộ nhớ, mở lại app thì về mở.
- **Esc:** thoát lệnh đang dùng về công cụ Chọn (bỏ hình dở nếu đang vẽ); đang ở Chọn thì bỏ chọn. `AnnotationEditor.Escape` (8 test).
- Thêm token `Gap.Right.SM`, `Gap.Bottom.SM`, icon `Icon.Pan`, `Icon.ChevronUp/Left/Right`.
- Test 500/500, build 0 cảnh báo. Chạy app thật: kéo trái ở mức vừa khung dịch ảnh; nút Pan nằm trên Phóng to và chọn được; G vào Pan; Esc từ Polygon đang vẽ, Box, Tag, Pan đều về Chọn và bỏ hình dở; thu gọn và mở lại panel và dải ảnh (ảnh chụp kiểm tra).
- **Lưu ý:** thanh công cụ nằm bên trái (theo thiết kế), không phải bên phải; nút Pan đã đặt vào đó. Khi thu gọn dải ảnh, panel Class vẫn chừa lề dưới cũ (168 px) và strip Kết thúc/Bỏ đỉnh/Hủy vẫn nằm cao hơn mép dưới. Chưa chỉnh.
- **Chưa kiểm tra:** thu gọn khi cửa sổ rất nhỏ; kéo bằng Pan trên ảnh rất lớn ở zoom cao; Esc khi hộp chọn class đang mở (phím tắt không chạy khi combobox có focus, nên Esc cũng không).

## Phiên tiếp: menu chuột phải trên nhãn (2026-11-07)

- Chuột phải lên nhãn: chọn nhãn rồi hiện menu ba mục (icon Chọn; icon + Class mở menu con liệt kê class; icon + Xóa đỏ). Xem `ANNOTATE.md`, mục "Menu chuột phải trên nhãn".
- Chép có chọn lọc `AppContextMenu` từ RoboStation (`sol-drcp/robo-station`, không sửa gì ở đó) sang `Controls/AppContextMenu.cs`, thêm `SubMenu` và `ItemWithIcon`. Template `MenuItem` ở `Menus.xaml` được thêm Popup cho menu con (trước đó ghi rõ là không hỗ trợ).
- `AnnotationEditor.LabelAt` (2 test). Test 502, build 0 cảnh báo.
- **Chạy app thật:** vẽ box, chuột phải trong lúc đang ở công cụ Box: đủ 3 mục; mở Class thấy connector và tape; chọn tape đổi class; mục Chọn đưa công cụ từ Box về Chọn; Xóa xóa nhãn (nhãn về 0); chuột phải chỗ trống không hiện menu. Ảnh chụp xác nhận giao diện.
- **Chưa kiểm tra:** menu trên nhãn bị khóa (logic làm mờ có, chưa bấm thử); phím mũi tên và Esc trong menu; nhãn polygon và rotated box (dùng cùng đường hit-test đã có test); nhãn chồng nhau (lấy nhãn trên cùng).

## Phiên: task mục tiêu của project (2026-11-08, nhánh `dev/data-studio`)

- **Quyết định 0014.** Project khai báo task mục tiêu (Detect, Segment, OBB, Classify), lưu ở `project_meta.target_task`, không thêm migration; project cũ không có khóa này nên chưa đặt task và mọi công cụ vẫn bật. `TargetTasks` (Domain), `ProjectSummary.TargetTask`, `ProjectService.SetTargetTaskAsync`, `IProjectStore.SetTargetTaskAsync`.
- **Project:** chọn task khi tạo (mặc định Segment, vì polygon xuất được cả Segment lẫn Detect) và đổi được ở project đang mở; đổi lưu ngay và không đụng tới nhãn.
- **Annotate:** `TaskTools.Allows` quyết định công cụ nào bật; rail làm mờ công cụ không hợp, tooltip nêu lý do, phím tắt và Q bỏ qua chúng.
- **Export:** `DatasetValidator` cảnh báo (không chặn) khi task của profile khác task của project; màn hình chọn sẵn profile cùng task; `version.json` ghi `targetTask`.
- **Kiểm tra:** build 0 cảnh báo; test Data Studio 523 (thêm 21 ở `Projects/TargetTaskTests.cs`: lưu và đọc lại task, đặt và xóa, từ chối Anomaly và project chưa mở, bảng công cụ theo task, editor từ chối công cụ và Q bỏ qua, đổi task khi đang dùng công cụ bị tắt, cảnh báo của validator). Chạy app thật qua UI Automation: ô task khi tạo project mặc định Segment; ở project Segment chỉ Chọn và Polygon bật, tooltip của Box nói "tắt vì project nhắm Segment", phím B không đổi công cụ, Q nhảy Chọn → Polygon rồi bỏ qua Box, OBB, Tag, phím P chọn Polygon; đổi sang Detect thì chỉ Box bật và vẽ được box (1 nhãn); đổi về "Chưa chọn" thì cả 4 công cụ bật.
- **Lỗi tìm được khi chạy app:** ô task của project đang mở trống vì setter bỏ qua giá trị khi đang đồng bộ. Đã sửa; test không bắt được vì `ProjectViewModel` chưa có test.
- **Chưa kiểm tra:** `version.json` có ghi `targetTask` hay không (chưa có test, chưa xuất lại bằng app); chọn Classify, OBB trong app; thay đổi task trong lúc đang vẽ polygon dở; project cũ mở bằng bản này (kiểm bằng test lưu trữ, chưa mở project thật). Không làm: khóa task ở cấp version trong DB, tự chuyển box thành polygon (xem quyết định 0014).
- Ổ D: đầy lúc build (0 byte trống); đã xóa `bin` và `obj` của thư mục chính theo ý bạn để lấy chỗ.

## Phiên: màn hình Cài đặt và phím tắt đổi được (2026-11-08, nhánh `dev/data-studio`)

- **Quyết định 0015.** Cài đặt theo người dùng ở `%APPDATA%\NTech\DataStudio\settings.json` (chỉ lưu phần đã đổi), phím tắt của Annotate đổi được, một phím một việc.
- **Code.** Application: `Shortcut`, `ShortcutAction` và `ShortcutCatalog` (23 hành động và phím mặc định), `ShortcutMap` (bất biến, `Find`, `With`, `Reset`, đọc file có xử lý trùng phím), `SettingsService`, `ISettingsStore`. Storage: `JsonSettingsStore` (ghi vào file tạm rồi đổi tên). Desktop: `SettingsViewModel` + `SettingsView` (tab "Cài đặt"), `ShortcutHints` (tooltip theo phím), `KeyNames`; `AnnotateViewModel.HandleKey` giờ tra `SettingsService` rồi chạy qua `Run`. Biểu tượng `Icon.Settings`.
- **Hành vi cũ giữ nguyên với phím mặc định:** V/S, B, P, R, T, G, Q, Shift+Q, +/-, A/D và ←/→, Enter, Backspace, Esc, Delete, N, H, F, Ctrl+Z/Y/Shift+Z/C/V; 1 đến 9 vẫn chọn class.
- **Kiểm tra.** Build 0 cảnh báo; test Data Studio 547 (thêm 24 ở `Settings/ShortcutSettingsTests.cs`: phím mặc định không trùng, tra phím và Shift, đổi, từ chối trùng và phím cố định, hoán đổi, đọc file hỏng và trùng, lưu rồi đọc lại, lỗi ghi). Chạy app thật qua UI Automation: đổi Box sang X (file `settings.json` được ghi), gán trùng bị từ chối kèm tên việc đang giữ phím, phím số bị từ chối, Esc hủy, gán Ctrl+Shift+K cho Polygon; ở Annotate phím B cũ không làm gì, X chọn Box, Ctrl+Shift+K chọn Polygon, tooltip hiện "Bounding box (X)"; tắt và mở lại app thì phím vẫn giữ; Mặc định và Đặt lại tất cả trả B và P. Script sao lưu và khôi phục `settings.json` thật.
- **Lỗi tìm được khi chạy app:** thông báo từ chối có đuôi "(Parameter 'keys')" của `ArgumentException`. Đã đổi sang `ShortcutException`.
- **Chưa kiểm tra:** nút Mặc định của dòng chưa đổi trông vẫn sáng như nút bấm được trong ảnh chụp (lệnh bị khóa, chưa xem style nút khóa); phím Enter/Tab/Space khi đang chờ phím (chưa bấm thử); phím có dấu hoặc bố cục bàn phím khác Mỹ; bộ gõ Telex bật lúc gán phím; mở app khi `settings.json` bị khóa hay hỏng (chỉ kiểm bằng test); `SettingsViewModel` không có test riêng.
- **Không làm:** nhiều phím cho một hành động từ giao diện, nhập/xuất cấu hình, phím tắt chuyển tab, mục cài đặt khác (xem quyết định 0015).
- **Sửa sau phản hồi:** lý do từ chối phím (ví dụ Space) trước hiện ở đầu trang, ngoài tầm nhìn khi đang ở cuối danh sách, và dòng thông báo bật tắt làm nội dung giật xuống. Giờ lý do hiện ngay dưới dòng đang gán và dòng trạng thái luôn chiếm chỗ cố định (kiểm bằng UI Automation: vị trí nút không đổi khi bấm Đổi phím và khi nhấn Space).
- **Space gán được (theo yêu cầu).** Bỏ quy tắc cố định của Space. Space đã gán chạy khi nhả phím và bỏ qua nếu đã kéo pan trong lúc giữ (`SpaceUsedForPan`); chưa gán thì chỉ để pan. Test 318 qua (đổi một test dùng Space làm phím hỏng sang `D5`, thêm test Space gán được). Chạy app thật: gán Space cho Box (hiện "Space"), chạm Space ở Annotate chọn Box, giữ Space kéo ảnh không đổi công cụ, chạm lại thì chọn Box; Đặt lại tất cả trả về B. **Chưa kiểm tra:** Ctrl+Space, giữ Space lâu (lặp phím), Space khi đang vẽ polygon dở, Space đang nằm trong ô gõ tên class.

## Phiên: Magic polygon (SAM) cho gán nhãn segment (2026-11-08, nhánh `dev/data-studio`)

- **Quyết định 0016.** Công cụ Magic polygon (phím M, biểu tượng đũa phép): rê chuột thấy vật thể được tách sẵn, bấm trái lấy thành nhãn Polygon, Shift+bấm và bấm phải thêm điểm trong/ngoài để tinh chỉnh. Chỉ bật khi project nhắm Segment (hoặc chưa chọn task) và đã có model. Bạn chọn SAM qua ONNX và điều kiện "project nhắm Segment" (không phải "sau vài ảnh đã gán").
- **Model** (đã tải theo sự cho phép của bạn): `mobile_sam_image_encoder.onnx` (28,2 MB) và `sam_mask_decoder_single.onnx` (16,5 MB) từ huggingface.co/Acly/MobileSAM vào `%LOCALAPPDATA%\NTech\DataStudio\models\sam\`. Không vào repo, không vào installer. Máy khác phải tự đặt hai file này (chưa có nút tải).
- **Code.** Application: `ISmartSegmenter`, `MagicPolygonController` (rê chuột kiểu kết quả mới nhất thắng), `AnnotateTool.MagicPolygon`, `AnnotationEditor` (`Magic`, `CommitPolygon`, `SecondaryDown`, `PointerDown(..., refine)`), `TaskTools`, hành động phím `MagicPolygonTool` (mặc định M). Imaging: `SamSegmenter` (ONNX Runtime + OpenCV; thêm gói Microsoft.ML.OnnxRuntime 1.28.0). Desktop: nút trên thanh công cụ, dòng trạng thái, vẽ đa giác xem trước và điểm trong `AnnotationCanvas`. `scripts\build.ps1` kiểm `onnxruntime.dll`.
- **Hợp đồng model dò bằng chạy thật** (xem quyết định 0016, mục 3): ảnh phải resize cạnh dài về 1024 trước bộ mã hóa; lần đầu đưa ảnh gốc, mask bị lệch tỉ lệ 0,78.
- **Kiểm tra.** Build 0 cảnh báo; test Data Studio 570 (thêm 18 ở `MagicPolygonTests` với segmenter giả: phân tích một lần, rê chuột kết quả mới nhất thắng, bấm tạo nhãn Polygon đúng class và chọn nó, thêm điểm trong/ngoài, thiếu model, lỗi segmenter, không có class, đổi công cụ và đổi ảnh; 5 ở `SamSegmenterTests` với model thật: khối đỏ và khối xanh ra đúng khung ±4 px, đường dẫn tiếng Việt, ảnh thứ hai thay ảnh đầu; sửa 1 test cũ vì thứ tự công cụ có thêm Magic). Chạy app thật qua UI Automation, cả bản Debug và **bản publish tự chứa** (`scripts\build.ps1` ra `onnxruntime.dll` 16 MB): project Segment, Box bị tắt, Magic bật; phân tích ảnh mất khoảng 2 giây; rê chuột lên khối đỏ hiện đa giác màu class; bấm tạo 1 nhãn Polygon (ảnh chụp thấy nhãn bám khít khối).
- **Tốc độ đo được (CPU máy này):** mã hóa ảnh 800x600 khoảng 1,4 giây; mỗi lần giải mã 0,1 đến 0,2 giây. Chưa đo trên ảnh lớn hơn.
- **Chưa kiểm tra:** chất lượng trên ảnh thật (chỉ thử ảnh tổng hợp khối màu trên nền nhiễu; dây điện mảnh, vật thể cùng màu nền, bóng đổ chưa thử); Shift+bấm và bấm phải trong app thật (kiểm bằng test với segmenter giả, chưa bấm bằng chuột thật); phím M; ảnh 20 MP; chạy trên máy chưa có model (chỉ kiểm bằng test: thông báo tên file thiếu và thư mục); đo độ trễ rê chuột cảm nhận; cài installer (chưa chạy installer vì chưa đóng gói; chỉ chạy `build.ps1`); nhiều ảnh lướt nhanh (mỗi ảnh mới phân tích lại, chưa phân tích trước ảnh kế).
- **Không làm:** nút tải model, GPU, tách theo hộp, lỗ trong polygon, tự gán cả thư mục (xem quyết định 0016).
- **Sửa sau phản hồi (model SAM):** bạn báo app đã cài vẫn "chưa có model". Trên máy của mình hai file có ở `%LOCALAPPDATA%\NTech\DataStudio\models\sam\` (kiểm bằng PowerShell và bằng chạy app), nên **chưa tái hiện được lỗi đó và chưa biết nguyên nhân**: có thể shell của mình ghi vào nơi khác với tiến trình bạn chạy, hoặc app bạn chạy là người dùng khác. Thay vì đoán, đã làm đường tải trong app (câu bạn hỏi): màn hình Cài đặt có mục Model Magic polygon với nút Tải model, tiến độ, Hủy, Mở thư mục; `SamModelService` + `SamModelDownloader` (kiểm SHA-256, ghi `.partial`). Không nhét model vào installer (xem quyết định 0016, mục 2).
- **Kiểm tra phần tải:** test +9 (`SamModelDownloaderTests`, máy chủ giả: ghi đủ hai file và tiến độ, bỏ qua file đúng, thay file sai, từ chối sai SHA-256 và không để file, mã lỗi 404, hủy không để lại gì; dịch vụ: thiếu, đang tải, đã có, lỗi rồi thử lại, hủy). Chạy app thật: dời tạm thư mục model đi, mở Cài đặt, thấy "Chưa có model", bấm Tải model, thấy "Đang tải ... 2.0 MB / 26.9 MB", xong báo "Đã có đủ hai file", hai file đúng cỡ và đúng SHA-256 (580f5fb6..., 93915fc7...), nút tải biến mất. Thư mục cũ mình chuyển sang thư mục tạm của phiên, không xóa.
- **Lỗi tìm được khi chạy app:** `ProgressBar.Value` ràng buộc hai chiều mặc định với thuộc tính chỉ đọc làm app hiện hộp thoại lỗi khi mở Cài đặt; đã đặt `Mode=OneWay`.
- **Chưa kiểm tra:** tải khi mất mạng giữa chừng và khi nút Hủy giữa file (chỉ kiểm bằng test); máy chưa từng có thư mục `models`; Annotate bật công cụ ngay sau khi tải xong mà không phải mở lại app (logic có, chưa bấm thử); nguyên nhân app bạn không thấy file.

## Phiên: kéo khung và zoom vùng cho Magic polygon (2026-11-08)

- **Lý do:** băng dính đen và dây thít SAM tách kém (nhỏ, ít tương phản, ảnh bị thu về 1024 px); bạn chọn làm kéo khung và phân tích theo vùng đang xem. Chi tiết và số đo ở cuối quyết định 0016.
- **Kéo khung:** kéo khung quanh vật rồi nhả; kết hợp với Shift+bấm/bấm phải. `AnnotationEditor.MagicDragBox`, `MagicMove`, `MagicUp`; `SegmentRequest(Points, Box)`.
- **Zoom vùng:** `MagicPolygonController.SetView` (canvas báo vùng đang thấy, chờ 0,3 giây), `ChooseRegion` (dưới 60% ảnh thì phân tích riêng phần đang xem, nới 25%, giữ vùng cũ khi còn đủ), `SamSegmenter` giữ hai khung (cả ảnh và vùng gần nhất) và ảnh đã giải mã. Canvas vẽ khung nét đứt khi kéo và đường chấm quanh vùng đang phân tích.
- **Kiểm tra.** Build 0 cảnh báo; test Data Studio 594 (Application 347, Imaging 90): thêm test khung, vùng, `ChooseRegion`, lặp 4 lần không lỗi ngẫu nhiên; với model thật: khung quanh khối, vùng cho đúng tọa độ ảnh gốc, điểm ngoài vùng không có đáp án, hai kết quả được giữ lại (đổi qua lại dưới 300 ms). Đo IoU trên ảnh tổng hợp khó (xem quyết định 0016). Chạy app thật qua UI Automation: phóng to bằng cuộn chuột hiện "Đang phân tích vùng đang xem...", xong thì rê chuột có xem trước, kéo khung quanh khối đỏ tạo 1 nhãn Polygon bám khít (chạy 2 lần, cả hai ra nhãn).
- **Lỗi/bẫy gặp khi thử:** lần đầu kéo quá nhanh bằng script thì không ra nhãn (cú kéo giả lập quá ngắn); kéo chậm hơn thì ra. Chưa rõ có phải giới hạn của script hay chuột thật cũng cần kéo đủ dài; ngưỡng là hơn hai lần sai số chọn.
- **Chưa kiểm tra:** ảnh thật (đặc biệt băng dính đen và dây thít, đúng lý do làm); ảnh rất lớn (20 MP) với vùng; độ trễ cảm nhận khi zoom liên tục (mỗi lần phân tích khoảng 1,5 giây và hover đứng lại trong lúc đó); chọn nhiều mặt nạ (chi tiết, một phần, cả vật) và model lớn hơn chưa làm.

## Bỏ đỉnh polygon (2026-11-08)

- Chọn polygon rồi bấm đúp lên đỉnh để bỏ đỉnh (`ShapeEditing.RemoveVertex`, gọi ở `AnnotationEditor.SelectDown`). Giữ tối thiểu 3 đỉnh, không để polygon tự cắt; bị từ chối thì có thông báo. Đi qua `EditRequest.Replace` nên undo và lưu như mọi sửa nhãn.
- Đã chạy test Domain (136) và nhóm AnnotationEditor (51), đều qua. Chưa chạy app thật để thử bấm đúp bằng chuột.

## Mở lại project gần nhất, mở toàn màn hình (2026-11-08)

- `settings.json` có thêm `LastProject` (đường dẫn thư mục project). `SettingsService.RememberProject` ghi mỗi khi project đang mở đổi; Đóng project thì xóa nên lần sau không tự mở. Mở thất bại (thư mục mất hoặc không phải project) thì hiện lỗi ở màn Project và giữ nguyên lựa chọn cũ. Mở lúc khởi động qua `ProjectViewModel.RestoreAsync`, nối ở `AppComposition`.
- `MainWindow` mở với `WindowState="Maximized"`.
- Kiểm tra: build 0 cảnh báo, toàn bộ test qua (Application 351, Domain 136). Chạy app thật qua UI Automation: cửa sổ Maximized, project tự mở với đủ số liệu; `LastProject` trỏ thư mục không tồn tại thì hiện "không phải project: thiếu project.db". Chưa thử đóng app rồi mở lại bằng tay sau khi Đóng project (có test cho phần lưu).

## Công tắc Kéo khung + zoom của Magic polygon (2026-11-08)

- Ô "Kéo khung + zoom" hiện khi chọn công cụ Magic polygon; `MagicPolygonController.BoxMode`, `AppSettings.MagicBoxMode`, `AnnotateViewModel.MagicBoxMode`. Bật: chỉ kéo khung mới lấy vật, có phân tích theo vùng zoom; tắt: rê chuột + bấm trên cả ảnh. Chi tiết ở cuối quyết định 0016.
- Test: Application 355 qua (thêm test cho hai chế độ, đổi chế độ, lưu). Chạy app thật qua UI Automation: bật thì rê và bấm không ra nhãn, kéo khung quanh khối đỏ ra 1 nhãn; tắt thì bấm ra thêm nhãn. Chưa thử trên ảnh thật.

## Gán nhãn tự động là tùy chọn nâng cao, thêm cách detect + SAM (2026-11-11, quyết định 0018)

- Cài đặt có thẻ "Tùy chọn nâng cao: gán nhãn tự động bằng model": công tắc `AutoLabelEnabled` (mặc định **tắt**), cách gán `AutoLabelMode` (`Segment` hoặc `DetectSam`) và model detect `AutoLabelDetectModel` riêng. Tắt thì Annotate không có nút Gán tự động / Gán tự động tất cả và `ConfiguredAutoLabeler` từ chối chạy. Ai đã chọn model theo 0017 phải bật công tắc một lần.
- Cách mới: `OnnxBoxDetector` + `YoloBoxDecoder` (Imaging) đọc ONNX detect của Trainer, `DetectThenSegmentLabeler` (Application) đưa từng box cho SAM, `SamRegionPlanner` quyết vùng phân tích (chỉ box dưới 24 px trong góc nhìn SAM có vùng riêng). SAM không cắt được thì giữ box thành polygon 4 đỉnh và báo số lượng cuối lượt gán.
- `IAutoLabeler` bỏ `ModelPath`/`SetModel` (model đặt qua `OnnxAutoLabeler`/`OnnxBoxDetector` ở composition root); `AutoLabel.IsBoxOutline`, `AutoLabelResult.BoxOutlineCount` mới.
- Test: Application 385, Imaging 115, toàn solution qua, build 0 cảnh báo. Chạy app thật qua UI Automation (bản Debug, project sao chép, settings.json được sao lưu và trả lại): tắt thì hai nút không có, bật thì có; đổi cách gán đổi tiêu đề, giải thích và trạng thái; `settings.json` ghi đúng; bấm Gán tự động khi chưa có model hiện "Chưa chọn model detect ...". Xem ảnh chụp: bố cục thẻ ổn.
- **Chưa chạy với model detect thật** (máy này không có file ONNX detect của Trainer): phần đọc ONNX chỉ test bằng tensor tổng hợp. Bước tiếp theo nên là chạy một model detect thật + `model.meta.json` của Trainer qua nút Gán tự động.
- Đo SAM thật trên wire harness (box lấy từ polygon tay): vùng phân tích không tốt hơn cả ảnh và chậm 4 đến 5 lần, nên chỉ dùng cho box nhỏ xíu; ngưỡng 24 px, bối cảnh quanh box và ngưỡng IoU 0,3 là số thử. Số và hạn chế của phép đo ở quyết định 0018. Test thật `DetectThenSegmentRealSamTests` chạy khi đặt `NTECH_SAM_HANDLABELS` (xem chú thích đầu file).
- Chưa làm: duyệt hàng loạt, chặn nhãn model chưa duyệt khi export, màu riêng cho nhãn model trên canvas, cách detect + SAM cho project detect (dùng thẳng box).

## Nhiều task, gán tự động Detect, Cài đặt chia mục, đóng gói .zip (2026-11-11, quyết định 0019 và 0020)

- **Project nhiều task**: `ProjectSummary.Tasks` (danh sách), khóa `target_tasks` (và `target_task` cũ giữ task đầu); `ProjectService.SetTargetTasksAsync`; `TaskTools.Allows(tasks, tool)`; `AnnotationEditor.TargetTasks`; Export cảnh báo khi profile ngoài tập, `version.json` có `targetTasks`. Form Project dùng ô tích (`TaskChoice`).
- **Gán tự động theo task**: `BoxAutoLabeler` (Detect ra box), `ConfiguredAutoLabeler.For(task)`, `AutoLabelService.LabelAsync(..., task)`, `AutoLabel.Shape`, ô "Gán tự động theo task" ở Annotate khi project có nhiều task, ngưỡng riêng `AutoLabelDetectMinScore`. "Xóa hết, gán mới" chỉ xóa nhãn cùng loại hình. Model detect dùng chung cho Detect và cho detect + SAM.
- **Cài đặt chia mục**: danh sách mục bên trái, thẻ Detect và Segment riêng có huy hiệu trạng thái (`AutoLabelSettingsViewModel`, `AutoLabelTaskPanel`, `Themes/Components/Settings.xaml`).
- **Đóng gói**: `IProjectPackager` / `ZipProjectPackager` (Storage) / `ProjectPackageService` / `ProjectPackageViewModel`; manifest `ntech-package.json`, backup SQLite, `.partial` rồi đổi tên, chống zip slip; thẻ "Đóng gói project" và "Mở từ file .zip" ở form Project.
- **Kiểm tra**: build 0 cảnh báo, toàn bộ test qua (Application 419, Imaging 115, Domain 136, Exporters 23, Trainer 64 và 36). Chạy app thật qua UI Automation (bản Debug, project sao chép, settings.json được sao lưu và trả lại): tích hai task, công cụ Box và Polygon cùng bật, ô chọn task hiện, nút Gán tự động báo thiếu model theo từng task, Cài đặt chia mục và huy hiệu đúng, thẻ Đóng gói hiện số liệu và đổi theo ô tích. Chạy thật packager trên bản sao project wire harness (không ảnh nguồn): 328 ảnh, 61 đã gán; gói 78 MB đóng 7,7 giây, giải nén 1,7 giây, số liệu và hash master khớp.
- **Lỗi tìm được nhờ chạy thật**: token `Layout.SettingsLabelWidth` ban đầu là `Double` mà `ColumnDefinition.Width` đòi `GridLength`, làm app thoát khi mở Cài đặt (build vẫn xanh). Đã đổi thành `GridLength`.
- **Chưa kiểm tra**: gán Detect với model detect thật (máy này không có ONNX detect); các hộp chọn file/thư mục của Windows và hộp xác nhận khi bấm Đóng gói / Mở từ file .zip chưa bấm thử bằng tay; gói 3,4 GB có ảnh nguồn của project thật. Cần người thử.
- **Lưu ý**: `AutoLabel`, `ProjectSummary`, `PackageManifest` tự định nghĩa so sánh bằng giá trị cho danh sách task. Số thứ tự quyết định 0019 và 0020 theo Data Studio; nhánh Trainer có thể đã dùng các số này (đã có sẵn trùng 0014 từ trước).
