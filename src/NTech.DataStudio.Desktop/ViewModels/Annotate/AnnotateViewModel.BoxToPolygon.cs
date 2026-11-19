using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Annotate;
using NTech.DataStudio.Desktop.Services;
using NTech.DataStudio.Domain.Annotations;
using NTech.DataStudio.Domain.Images;

namespace NTech.DataStudio.Desktop.ViewModels.Annotate;

/// <summary>Box to polygon with SAM, for one image or all of them (decision 0022): Detect labels become Segment labels in place.</summary>
public sealed partial class AnnotateViewModel
{
    private readonly BoxToPolygonConverter _boxConverter;

    /// <summary>The conversion makes polygons, so it is offered where the project can have them.</summary>
    public bool CanConvertBoxes => Application.Annotate.TaskTools.Allows(TargetTasks, AnnotateTool.Polygon);

    private bool CanConvertOne() => !IsAutoLabeling && _current is not null && Editor?.ConvertibleBoxes().Count > 0;

    private bool CanConvertAll() => !IsAutoLabeling && HasImages;

    [RelayCommand(CanExecute = nameof(CanConvertOne))]
    private async Task ConvertBoxesAsync()
    {
        if (_current is not { } row || Editor is not { Document: { } document } editor) return;

        var boxes = editor.ConvertibleBoxes();
        if (boxes.Count == 0) return;
        if (_boxConverter.Unavailable is { } why)
        {
            AutoLabelMessage = why;
            return;
        }

        IsAutoLabeling = true;
        AutoLabelMessage = $"Đang chuyển {boxes.Count} box thành polygon...";
        try
        {
            var conversion = await _boxConverter.ConvertAsync(row.MasterFullPath, document.Width, document.Height, boxes, _service!.Now);
            if (!ReferenceEquals(row, _current) || !ReferenceEquals(editor, Editor) || editor.Document?.ImageId != row.Id)
            {
                AutoLabelMessage = null;
                return;
            }

            AutoLabelMessage = editor.ApplyConversion(conversion)
                ? ConversionText(conversion.CutOut, conversion.RectangleOnly, 1) + " Hãy kiểm tra rồi bấm Duyệt ảnh."
                : editor.Message ?? "Không lưu được nhãn.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            AutoLabelMessage = $"Chuyển box lỗi: {exception.Message}";
        }
        finally
        {
            IsAutoLabeling = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConvertAll))]
    private async Task ConvertAllBoxesAsync()
    {
        if (_service is null || Editor is null) return;
        if (_boxConverter.Unavailable is { } why)
        {
            AutoLabelMessage = why;
            return;
        }

        // Only images that hold boxes; read one by one, since the strip does not know the shape of a label.
        var targets = new List<(ImageRowViewModel Row, int Boxes)>();
        foreach (var row in _allImages)
        {
            if (ReferenceEquals(row, _current) && Editor.Document is not null)
            {
                var own = Editor.ConvertibleBoxes().Count;
                if (own > 0) targets.Add((row, own));
                continue;
            }

            var count = _service.LoadContent(row.Id).Annotations.Count(annotation => annotation.Kind == ShapeKind.Box);
            if (count > 0) targets.Add((row, count));
        }

        if (targets.Count == 0)
        {
            AutoLabelMessage = "Không có ảnh nào có box để chuyển.";
            return;
        }

        var total = targets.Sum(item => item.Boxes);
        if (!_prompt.Confirm(
                "Chuyển box thành polygon",
                $"Chuyển {total} box của {targets.Count} ảnh thành polygon bằng SAM?\n\n"
                + "Mỗi box được thay bằng polygon cùng class: SAM cắt vật thể trong box, và nếu không cắt được thì box thành polygon hình chữ nhật. "
                + "Sau đó ảnh chỉ còn polygon. Mỗi ảnh là một lần Hoàn tác. Ảnh đã duyệt thành \"cần review\" và nhãn do SAM có chấm tím.\n\n"
                + "Dataset Detect vẫn xuất được từ polygon (mỗi polygon thành box bao quanh).",
                "Chuyển"))
            return;

        IsAutoLabeling = true;
        IsBatchRunning = true;
        _batchCts = new CancellationTokenSource();
        var token = _batchCts.Token;
        var generation = _loadVersion;
        int done = 0, cut = 0, rectangles = 0, failed = 0;
        string? firstError = null;
        var cancelled = false;
        try
        {
            foreach (var (row, _) in targets)
            {
                token.ThrowIfCancellationRequested();
                AutoLabelMessage = $"Chuyển box {done + 1} / {targets.Count}: {row.FileName}";
                try
                {
                    var conversion = await ConvertRowAsync(row, token);
                    if (generation != _loadVersion || _service is null) return;
                    if (conversion is not null)
                    {
                        cut += conversion.CutOut;
                        rectangles += conversion.RectangleOnly;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failed++;
                    firstError ??= $"{row.FileName}: {exception.Message}";
                }

                done++;
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        finally
        {
            IsBatchRunning = false;
            IsAutoLabeling = false;
            _batchCts?.Dispose();
            _batchCts = null;
        }

        if (generation != _loadVersion) return;

        RefreshCounts();
        UpdateProjectProgress();
        ShowImageList();
        var text = $"{(cancelled ? "Đã dừng sau" : "Xong:")} {ConversionText(cut, rectangles, done - failed)}";
        if (failed > 0) text += $" {failed} ảnh lỗi (đầu tiên: {firstError}).";
        AutoLabelMessage = text;
    }

    /// <summary>The image on screen goes through its editor; any other through its own document, as one edit either way.</summary>
    private async Task<BoxConversion?> ConvertRowAsync(ImageRowViewModel row, CancellationToken token)
    {
        if (ReferenceEquals(row, _current) && Editor is { Document: { } shown } editor && shown.ImageId == row.Id)
        {
            var own = editor.ConvertibleBoxes();
            if (own.Count == 0) return null;
            var result = await _boxConverter.ConvertAsync(row.MasterFullPath, shown.Width, shown.Height, own, _service!.Now, token);
            return editor.ApplyConversion(result) ? result : throw new InvalidOperationException(editor.Message ?? "Không lưu được nhãn.");
        }

        var document = _service!.Open(row.Image);
        var boxes = document.Annotations.Where(annotation => annotation.Kind == ShapeKind.Box).ToList();
        if (boxes.Count == 0) return null;

        var conversion = await _boxConverter.ConvertAsync(row.MasterFullPath, document.Width, document.Height, boxes, _service.Now, token);
        _service.Execute(document, new EditRequest(conversion.Boxes, conversion.Polygons, Array.Empty<long>(), Array.Empty<long>()));
        row.Update(document);
        return conversion;
    }

    private static string ConversionText(int cut, int rectangles, int images)
    {
        var text = $"đã chuyển {cut + rectangles} box thành polygon";
        if (images > 1) text += $" ở {images} ảnh";
        text += $" ({cut} do SAM cắt";
        text += rectangles > 0 ? $", {rectangles} giữ hình chữ nhật vì SAM không cắt được: hãy sửa tay)." : ").";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
