# Design system

Cùng ngôn ngữ thiết kế với robo-station. Giá trị nằm ở `src/NTech.DataStudio.Desktop/Themes/Tokens/`; bảng màu đầy đủ ở mục Design system của [tài liệu thiết kế](../DESIGN_DOC.md).

## Token

| File | Nội dung |
| --- | --- |
| `Colors.xaml` | Nền, viền, chữ, brand, status. Đã bỏ nhóm Mission, Robot, Zone, Fleet, GeoMeasure, Chart. |
| `ColorsOverlay.xaml` | Nền trong suốt cho TopBar, panel, widget nổi trên canvas |
| `Typography.xaml` | Segoe UI, Cascadia Mono; cỡ chữ Display 28 tới Micro 11 |
| `Spacing.xaml` | `Spacing.*` (4 tới 32), `Radius.*`. Thêm `Inset.*` và `Gap.*` kiểu Thickness để View không phải hard-code margin. |
| `Sizing.xaml` | `ControlHeight.*`, `IconSize.*`, `Layout.*` |
| `Elevation.xaml` | Đổ bóng cho lớp nổi |
| `Icons.xaml` | Icon dạng nét, lưới 18×18. Thêm icon các view và Select, Box, Polygon, Obb, Tag. |
| `Annotation.xaml` | Màu class 1–10, nét vẽ, handle, crosshair, chấm trạng thái ảnh |

## Component

| File | Style chính |
| --- | --- |
| `Buttons.xaml` | `ViewSelector.Item`, `ToolRail.Button`, `ToolRail.ToggleButton`, `Button.Primary`, `Button.Secondary`, `Button.Attention`, `Button.Danger` |
| `Inputs.xaml` | `Input.Text`, `Input.Combo`, `Input.CheckBox` |
| `DataGrid.xaml` | Bảng và ô số liệu |
| `Menus.xaml` | ContextMenu |
| `Overlay.xaml` | `Overlay.TopBar`, `Overlay.ToolRail`, `Overlay.FloatingPanel`, `Overlay.Strip` (như FloatingPanel nhưng không cố định bề rộng), `Surface.Card`, `Badge.Pill` |
| `ScrollBars.xaml` | ScrollBar |
| `Progress.xaml` | ProgressBar |
| `Lists.xaml` | ListBox và ListBoxItem (nền trong suốt, hàng chọn dùng `Background.Selected`) |
| `Slider.xaml` | Slider |
| `Text.xaml` | `Text.PageTitle`, `Text.SectionHeader`, `Text.Body`, `Text.Secondary`, `Text.Caption`, `Text.Label`, `Text.Metric`, `Text.MetricSmall` |

Style riêng của robot, bản đồ và video không được copy sang.

## Trạng thái ảnh

| Trạng thái | Màu |
| --- | --- |
| New | `Status.Neutral` |
| Labeled | `Status.Info` |
| Reviewed | `Status.Success` |
| Null | `Status.Neutral`, viền đứt |
| Rejected | `Status.Error` |
