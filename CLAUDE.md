# CLAUDE.md

## Vai trò

Bạn là Claude Code, làm việc như senior coding assistant trong repo `NTech-Data-Studio`. Giữ đúng định hướng sản phẩm và kiến trúc đã ghi trong repo.

## Thứ tự đọc

1. `README.md`
2. `docs/README_AI_WORKFLOW.md`
3. `docs/00_CONTEXT.md`
4. `tasks/active.md`
5. `handoff/latest.md` với Data Studio, `handoff/trainer.md` với Trainer
6. `decisions/0000_INDEX.md`
7. `docs/DESIGN_DOC.md`, và `docs/design/` khi đụng tới UI

## Quy tắc repo

- Không đổi kiến trúc mà không ghi vào `decisions/`.
- Không sửa file không liên quan; không refactor lớn khi chưa được đồng ý.
- Trước khi kết thúc phiên: cập nhật file bàn giao của phần đang làm (`handoff/latest.md` hoặc `handoff/trainer.md`), và `tasks/active.md`, `docs/00_CONTEXT.md` nếu trạng thái đổi.
- Data Studio và Trainer phát triển song song theo quy tắc trong `docs/README_AI_WORKFLOW.md`. Commit chỉ gồm file của phần mình; đừng dùng `git add -A` khi còn thay đổi của phiên khác.
- Đổi UI hoặc hành vi runtime thì phải chạy app; build xanh không chứng minh giao diện đúng.
- Không sửa gì trong robo-station. Chỉ copy có chọn lọc sang đây.

## Ràng buộc kiến trúc

- .NET 10, C#, WPF, MVVM, `CommunityToolkit.Mvvm`. MVP không dùng Python.
- Hướng phụ thuộc: `Desktop → Application → Domain`. `Imaging`, `Storage`, `Exporters` hiện thực abstraction của Application.
- Namespace và assembly luôn là `NTech.DataStudio.*`, kể cả Trainer sau này (`NTech.DataStudio.Trainer`). Không dùng `NTech.NVision.*`.
- NVision chỉ nhận file `.nvmodel`; không đọc dataset, không gọi vào Data Studio.
- Giữ composition root thủ công ở `Bootstrap/AppComposition.cs`.
- Ảnh gốc không bao giờ bị sửa. Nhãn lưu theo pixel trên ảnh master.
- MVP chỉ xuất file định dạng YOLO; không nhúng code Ultralytics (AGPL-3.0). Không dùng ImageSharp.

## Quy tắc code và UI

- Logic nằm trong ViewModel, dùng binding và `ICommand`. Code-behind chỉ để xử lý chuột và vẽ trên canvas.
- View chỉ dùng token và style trong `Themes/`; không hard-code màu, khoảng cách.
- Mỗi màn hình có đủ trạng thái loading, empty, error.
- Không dùng emoji thay icon, không gradient, không shadow đậm, không bo góc lớn.
- Không hiển thị dữ liệu mẫu như dữ liệu thật.

## Bẫy đã biết

- Trong code Desktop phải viết đầy đủ `System.Windows.Application`, vì namespace `NTech.DataStudio.Application` che mất tên `Application`.
- Dictionary trong `Themes/Components` tự merge `_Tokens.xaml` để `StaticResource` resolve được. Giữ pattern này.
- Thiếu token chỉ lỗi lúc chạy, không lỗi lúc build.
- `ProjectDb` tắt pooling để project có thể chép đi ngay sau khi đóng. Đừng bật lại.
- Migration đã phát hành không được sửa, chỉ được thêm mới.
- Không dùng `Cv2.ImRead`/`ImWrite`: chúng không mở được đường dẫn tiếng Việt. Đọc ghi qua `byte[]` với `ImDecode`/`ImEncode`.
- Đừng đặt tác dụng phụ trong đối số của `progress?.Report(...)`: khi `progress` là null, đối số không được tính.
- Chọn tab qua `IsSelected`, không qua `Command`: RadioButton có thể được chọn mà không có click.
- Mọi sửa nhãn đi qua `AnnotateService.Execute` (xem quyết định 0006). Đừng đổi `ImageDocument` hay ghi bảng `annotations` ở chỗ khác, nếu không undo và lưu sẽ lệch nhau.
- Màn hình có giữ connection (Annotate) phải thành `IActivatable` và nhả connection khi rời đi. `ShellViewModel` gọi `Activate` và `Deactivate`.
- `AnnotationCanvas` vẽ nhãn theo tọa độ màn hình để nét vẽ luôn dày như nhau khi zoom; chỉ ảnh dùng transform. Đừng chuyển nhãn sang hệ tọa độ ảnh.
- Phím tắt của Annotate đi qua `AnnotateViewModel.HandleKey`, gọi từ `PreviewKeyDown` của view. Đừng dùng `KeyBinding`: chữ đơn sẽ bị bắt cả khi đang gõ tên class.
- Cái gì phải tái lập (chia tập, mẫu tăng cường, nhiễu) thì dùng `DeterministicRandom`, không dùng `System.Random` hay `string.GetHashCode` (xem quyết định 0007). Test `Sample_PinnedValuesDoNotDriftBetweenRuntimeVersions` ghim dãy số; sửa nó là phá mọi version đã lưu.
- Ma trận affine ở tọa độ cạnh (0..W), cùng hệ với nhãn; đưa sang OpenCV phải qua `Affine2D.ToPixelCenters()`. Ảnh và nhãn phải cùng đọc một `RenderPlan`: đừng tính ma trận riêng cho nhãn.
- `Mat.SetArray` chỉ nhận Mat một kênh; muốn nhiều kênh thì tạo Mat `cols * channels` rồi `Reshape(channels)`. `Cv2.SetRNGSeed` không có trong OpenCvSharp.
- Mở thêm màn hình dùng `ComboBox` với `Option<T>`: UI Automation đọc tên mục qua `ToString`, nên `Option<T>.ToString` trả về `Label`. Giữ như vậy.
- Panel và dải nổi trên canvas che mất click: điểm thử bằng chuột trong kịch bản UI phải nằm ngoài chúng.
- Xuất dataset: ảnh và nhãn của mỗi bản sao phải đọc cùng một `RenderPlan` (như preview); đừng dựng nhãn riêng. Hình nhãn (box, polygon, OBB) chỉ quyết định ở `LabelShapes` để mọi định dạng cùng một cách; OBB nhô ra ngoài ảnh phải co về tâm, đừng kẹp góc (quyết định 0008). Ghi vào `.partial` rồi đổi tên; ghi đè chỉ áp cho thư mục có `version.json`.
- `Parallel.ForEach` bọc lỗi của worker trong `AggregateException`; `ExportService` mở nó ra, nên người gọi bắt `ImageReadException` hay `InvalidOperationException` trực tiếp. Giữ nguyên khi sửa.
- Test cần nhiều tập (train, val, test) thì mỗi ảnh phải có `source_image_id` riêng: ảnh cùng nguồn luôn cùng một tập, nên `AnnotateFixture` mặc định (một nguồn cho mọi ảnh) cho ra toàn train.
- Phím tắt của Annotate là chữ đơn nên bộ gõ tiếng Việt Telex (Unikey, EVKey) làm hỏng chúng (gõ `e` hai lần thành `ê`, app nhận Backspace). Tắt IME ở WPF không chặn được bộ gõ ở mức hệ điều hành. Đừng gán phím chữ lặp được hoặc là phím Telex (a, e, o, d, w, s, f, r, x, j, z) cho việc cần nhấn liên tiếp; Q dùng để đổi công cụ vì Telex không đụng tới nó. Khi tự kiểm bằng SendKeys trên máy có Telex bật, thấy phím rơi thì xem nó có thành Backspace không.
- Menu chuột phải dựng bằng `Controls/AppContextMenu` (từ RoboStation); mục có con tự hiện mũi tên và mở menu con nhờ Popup trong template `MenuItem` ở `Menus.xaml`. Mục không có header cần tooltip để có tên cho trình đọc màn hình.

## Bẫy đã biết của Trainer

- Trên Linux, solution có WPF không build được: dùng `NTech.DataStudio.Trainer.slnf`. Trainer không tham chiếu project nào của Data Studio.
- Worker chỉ gọi Ultralytics trong `ultralytics_adapter.py` và `onnx_export.py`. Không đưa code Ultralytics vào C# hay installer (quyết định 0009).
- Worker dùng thư viện nào thì khai báo trong `docker/trainer/requirements.in`, đừng dựa vào `ultralytics` kéo theo: nó không kéo `pyyaml`. Build lại image rồi chạy thật; test không chạy trong image nên không bắt được lỗi này.
- Khi run kết thúc, Ultralytics xóa optimizer khỏi `last.pt`, nên dừng giữa chừng phải giữ `checkpoints/resume.pt` trước khi đặt `trainer.stop`.
- `on_fit_epoch_end` còn được gọi một lần ở lượt đánh giá cuối, với `epoch + 1` và không có val loss. Bỏ qua lượt này, nếu không sẽ có "Epoch 4/3" và cảnh báo NaN giả.
- ONNX xuất bằng `nms=True` cho mọi phiên bản (quyết định 0012). `nms=False` là nhánh khác với metric lúc train. Kiểm chứng phải cắt box về khung đầu vào, vì NMS nhúng trả cả box vượt ra ngoài.
- Worker báo trạng thái kết thúc trước khi container thoát hẳn: lệnh kế tiếp phải chờ container thoát. Khi `resume` hoặc `export`, đọc sự kiện từ cuối file, nếu không sẽ đọc nhầm trạng thái của lần trước.
- Dung lượng RAM, VRAM, đĩa tính theo 1024³ ở cả C# và Python, để khớp số Windows và `nvidia-smi` hiện.
- Mount bằng `--mount`, không dùng `-v`: `-v` tách theo dấu `:` của đường dẫn Windows.
- Dataset tổng hợp phải giữ `ntech_synthetic: true` để mọi báo cáo ghi rõ. Không trình bày metric của nó như kết quả thật, và không commit ảnh của project wire harness.
- Desktop của Trainer là Avalonia (quyết định 0013). Logic dùng chung đặt ở `NTech.DataStudio.Trainer`, không đặt trong CLI hay ViewModel. ViewModel không dùng kiểu của Avalonia; hộp chọn file đi qua `IDialogs`.
- Avalonia dùng compiled binding: đường dẫn qua một giá trị null (`Report.Ok` khi chưa có `Report`) sinh lỗi binding. Hãy đưa ra thuộc tính phẳng không null. `ScreenTests` báo mọi lỗi binding, nên đừng nới bộ lọc ở đó.
- Trong `Content` của CheckBox và Button, Avalonia hiểu `_` là phím tắt và nuốt mất nó: viết `hsv__h` để hiện `hsv_h`.
- Selector nhiều loại (`TextBox, ComboBox`) trong một `Style` chỉ nhận thuộc tính của lớp cha chung, nên tách thành từng style riêng.
- Tên icon trùng với `Icons.xaml` của WPF phải cùng hình (`TokenParityTests` kiểm); icon riêng của Trainer đặt tên mới, ví dụ `Icon.Grade.*`.
- Test Desktop đặt `Polling: false` trong `TrainerServices` rồi tự gọi `RefreshAsync`; để timer chạy trong test sẽ có hai lượt đọc chen nhau.
- Test giao diện dùng `HeadlessUnitTestSession` trực tiếp vì repo dùng xunit 2; `Avalonia.Headless.XUnit` cần xunit v3.
- Trên Linux, app cần thư viện X11 của hệ điều hành (`libice6`, `libsm6` thiếu trong WSL Ubuntu).
- Lượt làm mới định kỳ gọi `TryAsync(..., refresh: true)`: nó chỉ xóa lỗi do chính nó gây ra. Đừng bỏ cờ này, nếu không lỗi của nút bấm biến mất sau 2 giây.
- Workspace không chuyển được giữa Windows và Linux: `workspace.json` giữ đường dẫn tuyệt đối của dataset. Mọi chỗ mount dataset phải gọi `Workspace.RequireDataset()` trước.
- Trong WSLg cửa sổ app không có cây UI Automation: chỉ kiểm được bằng ảnh chụp và bấm theo tọa độ.
- Segment không-end2end (YOLO11, YOLOv8): `predict` của Ultralytics trả tập box khác NMS nhúng trong ONNX, dù file ONNX đúng (PyTorch thô cộng NMS khớp 300/300 hàng). Kiểm chứng ONNX segment đối chiếu mô hình thô và ghép box chưa cắt về khung, đừng đổi sang `predict` (quyết định 0014). Chạy `docker run` trong Git Bash nhớ `MSYS_NO_PATHCONV=1`, nếu không đường dẫn container bị đổi thành đường dẫn Windows.

## Kiểm tra

```powershell
dotnet build NTech.DataStudio.slnx -c Debug
dotnet test NTech.DataStudio.slnx
dotnet run --project src\NTech.DataStudio.Desktop
```

Đổi script, `installer/NTech-DataStudio.iss` hay cách publish thì chạy `scripts\deploy.bat -SkipTests` và cài thử installer vào thư mục tạm (`/VERYSILENT /CURRENTUSER /DIR=...`), rồi chạy app đã cài và gỡ ra. Đừng đóng gói hay chép installer đi nơi khác khi chưa được yêu cầu.

Báo đúng những gì đã chạy và kết quả, cùng những gì còn cần người kiểm tra.

## Phát hành

- Version chỉ khai báo ở `Directory.Build.props`. Đừng ghi cứng version vào script hay `.iss`.
- `AppId` trong `.iss` không bao giờ đổi, vì installer mới dùng nó để nhận ra bản đã cài và nâng cấp. Trainer có `.iss` và AppId riêng (`trainer/installer/NTech-Trainer.iss`); đừng chép AppId giữa hai file.
- File `.ps1` giữ ASCII, vì Windows PowerShell 5.1 đọc file không có BOM theo bảng mã ANSI. File `.bat` giữ CRLF (`.gitattributes` đã quy định).
- Thêm file vào thư mục cài (profile mới, native DLL) thì phải khớp với danh sách kiểm tra ở cuối `scripts\build.ps1`.
