# 0016. Magic polygon: tự tách vật thể bằng SAM chạy qua ONNX

Ngày: 2026-11-08

## Bối cảnh

Gán nhãn segment bằng tay (từng đỉnh polygon) quá chậm. Roboflow có công cụ Smart Polygon: rê chuột thì thấy vật thể được tách sẵn, bấm là lấy. Cần cùng trải nghiệm đó, chạy offline trong app WPF (không Python, quyết định 0001).

## Quyết định

1. **Dùng Segment Anything (MobileSAM) xuất ONNX, chạy bằng ONNX Runtime trên CPU.** Hai file: bộ mã hóa ảnh `mobile_sam_image_encoder.onnx` (28 MB) chạy một lần cho mỗi ảnh (khoảng 1,5 đến 2 giây), bộ giải mã `sam_mask_decoder_single.onnx` (16 MB) chạy mỗi lần rê chuột (khoảng 0,1 đến 0,2 giây). Nguồn: huggingface.co/Acly/MobileSAM (MIT theo trang model; MobileSAM gốc Apache-2.0). Thêm gói `Microsoft.ML.OnnxRuntime` 1.28.0 (MIT) vào `NTech.DataStudio.Imaging`; `onnxruntime.dll` (16 MB) vào danh sách kiểm tra của `scripts\build.ps1`.
2. **File model không nằm trong repo và không vào installer; app kiểm sau khi cài và để người dùng bấm tải.** Đặt ở `%LOCALAPPDATA%\NTech\DataStudio\models\sam\`. Thiếu file thì công cụ bị làm mờ, tooltip ghi lý do và chỉ sang Cài đặt. Màn hình Cài đặt có mục "Model Magic polygon (SAM)": trạng thái, đường dẫn, nút **Tải model (44,7 MB)** có thanh tiến độ và nút Hủy, nút Mở thư mục model (cho máy không có mạng: chép tay hai file vào). `SamModelDownloader` ghi vào `.partial`, kiểm SHA-256 của đúng hai file đã thử với app, rồi mới đổi tên; sai hash thì bỏ, không để lại gì. App không bao giờ tự tải: chỉ khi người dùng bấm. Lý do không nhét vào installer: thêm 45 MB cho mọi người dù không dùng segment, đổi model thì phải phát hành lại app, và license model là điều kiện riêng của model chứ không phải của app. Đổi lại cần mạng một lần.
3. **Hợp đồng của model đã dò bằng chạy thật, không đoán:** bộ mã hóa nhận RGB 0..255 dạng float, chiều cao x rộng x 3, **đã resize cạnh dài về 1024** (nó tự chuẩn hóa và đệm bên trong; đưa ảnh gốc chưa resize cho mask lệch tỉ lệ 0,78). Bộ giải mã nhận điểm theo hệ tọa độ 1024, thêm một điểm đệm nhãn -1, nhãn 1 là trong vật thể, 0 là ngoài; `orig_im_size` là kích thước ảnh đã resize nên mask ra ở cỡ 1024, không phải cỡ master. Ảnh master 20 MP vì thế không tốn thêm bộ nhớ. Kết quả được đưa về điểm ảnh master.
4. **Công cụ mới `AnnotateTool.MagicPolygon`** (phím M, đổi được ở Cài đặt, biểu tượng đũa phép), **chỉ bật khi project nhắm Segment** hoặc chưa chọn task (quyết định 0014), và khi có model. Bạn chọn điều kiện này thay cho "sau vài ảnh đã gán": SAM không học từ nhãn của bạn nên không cần chờ.
5. **Cách dùng:** rê chuột thấy đa giác xem trước (màu class đang chọn); bấm trái để lấy thành nhãn Polygon thường (sửa đỉnh, undo, lưu như mọi nhãn); Shift+bấm thêm điểm "trong vật thể", bấm phải thêm điểm "ngoài vật thể" để tinh chỉnh trước khi lấy; Backspace bỏ điểm vừa thêm; Esc về công cụ Chọn. Ở công cụ này bấm phải không mở menu nhãn.
6. **Tách theo lớp:** `ISmartSegmenter` ở Application (ranh giới), `SamSegmenter` ở Imaging (ONNX + OpenCV), `MagicPolygonController` ở Application (trạng thái, rê chuột kiểu "kết quả mới nhất thắng", sự kiện về luồng giao diện). Nhờ vậy toàn bộ logic công cụ test được bằng segmenter giả, không cần model.
7. **Đường viền:** lấy vùng liên thông chứa điểm trong đầu tiên (nếu không thì vùng lớn nhất), bỏ lỗ bên trong, đơn giản hóa bằng `ApproxPolyDP` với sai số 0,25% chu vi (kẹp 0,75 đến 4 px ở cỡ 1024). Nếu đường đơn giản hóa tự cắt thì dùng bao lồi.

## Không làm (cố ý)

- Không bundle model vào installer, không tự tải ngầm, không tự cập nhật model.
- Không dùng GPU (DirectML/CUDA); CPU đủ cho rê chuột mượt nhờ chạy nền và bỏ bớt vị trí trung gian.
- Không có tách theo hộp (box prompt), không SAM2/SAM lớn, không lưu mask điểm ảnh, không lỗ bên trong polygon (polygon của app không có lỗ).
- Không tự gán nhãn hàng loạt cả thư mục.

## Hệ quả

- Chất lượng phụ thuộc ảnh thật; mới thử trên ảnh tổng hợp (khối màu trên nền nhiễu, sai lệch không quá vài px). Dây điện mảnh hoặc chi tiết nhỏ có thể cần thêm điểm, hoặc sửa đỉnh sau.
- Địa chỉ tải (`resolve/main` của Acly/MobileSAM) có thể đổi hay biến mất; khi đó SHA-256 không khớp hoặc 404 và màn hình báo lỗi, người dùng vẫn chép tay được.
- Ảnh đầu tiên sau khi bật công cụ phải chờ vài giây phân tích; app hiện "Đang phân tích ảnh..." và chuyển ảnh thì phân tích lại (chưa phân tích trước ảnh kế).
- Test dùng model thật (`SamSegmenterTests`) trả về sớm trên máy không có model, nên "xanh" trên máy khác không chứng minh pipeline đúng; chạy trên máy có model.
