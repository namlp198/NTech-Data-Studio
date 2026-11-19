using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NTech.DataStudio.Desktop.ViewModels.Annotate;

/// <summary>The thumbnail strip as a list to work on: select for the dataset in bulk, and delete images (decision 0022).</summary>
public sealed partial class AnnotateViewModel
{
    private IReadOnlyList<ImageRowViewModel> _stripSelection = Array.Empty<ImageRowViewModel>();
    private IReadOnlyList<long> _lastDeleted = Array.Empty<long>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDeleted))]
    private int _deletedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndoDelete))]
    private string? _deleteMessage;

    public bool HasDeleted => DeletedCount > 0;
    public bool HasUndoDelete => DeleteMessage is not null && _lastDeleted.Count > 0;

    /// <summary>The thumbnails the user has selected (Ctrl or Shift click), reported by the view.</summary>
    public void SetStripSelection(IEnumerable<ImageRowViewModel> rows)
    {
        _stripSelection = rows.ToList();
        DeleteImagesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What these buttons can do depends on the image open and its labels, which change under them.</summary>
    private void RaiseImageCommandsChanged()
    {
        ConvertBoxesCommand.NotifyCanExecuteChanged();
        ConvertAllBoxesCommand.NotifyCanExecuteChanged();
        DeleteImagesCommand.NotifyCanExecuteChanged();
    }

    private void RefreshDeletedCount() => DeletedCount = _service?.LoadDeletedIds().Count ?? 0;

    // ---- select for the dataset (only the images the filters show) ----

    [RelayCommand]
    private void SelectAllShown() => SetShownSelection(_ => true);

    [RelayCommand]
    private void ClearShownSelection() => SetShownSelection(_ => false);

    [RelayCommand]
    private void InvertShownSelection() => SetShownSelection(row => !row.InDataset);

    private void SetShownSelection(Func<ImageRowViewModel, bool> wanted)
    {
        if (_service is null) return;

        var changes = Matching().Select(row => (Row: row, Value: wanted(row))).Where(item => item.Value != item.Row.InDataset).ToList();
        if (changes.Count == 0) return;

        try
        {
            var on = changes.Where(item => item.Value).Select(item => item.Row.Id).ToList();
            var off = changes.Where(item => !item.Value).Select(item => item.Row.Id).ToList();
            if (on.Count > 0) _service.SetInDatasetMany(on, true);
            if (off.Count > 0) _service.SetInDatasetMany(off, false);
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            ErrorMessage = $"Không lưu được lựa chọn ảnh: {exception.Message}";
            return;
        }

        foreach (var (row, value) in changes)
            row.SetInDatasetWithoutNotifying(value);
        UpdateSelectionText();
    }

    // ---- delete and restore ----

    private bool CanDeleteImages() => _service is not null && _current is not null && !IsAutoLabeling;

    private List<ImageRowViewModel> DeleteTargets() =>
        _stripSelection.Count > 1 ? _stripSelection.ToList() : _current is null ? new() : new() { _current };

    [RelayCommand(CanExecute = nameof(CanDeleteImages))]
    private async Task DeleteImagesAsync()
    {
        if (_service is null || Editor is null) return;

        var rows = DeleteTargets();
        if (rows.Count == 0) return;

        var labels = rows.Sum(row => row.LabelCount);
        var what = rows.Count == 1 ? $"ảnh {rows[0].FileName}" : $"{rows.Count} ảnh đã chọn";
        var message = $"Xóa {what} khỏi project?\n\n"
            + "Ảnh biến khỏi danh sách, khỏi dataset mới và khỏi các lần xuất. Version đã tạo sẽ bỏ ảnh này khi xuất. "
            + "File ảnh và nhãn vẫn còn trên đĩa nên khôi phục được bằng nút \"Khôi phục ảnh đã xóa\"."
            + (labels > 0 ? $"\nẢnh có {labels} nhãn và tag; chúng không được dùng nữa cho tới khi khôi phục." : string.Empty);
        if (!_prompt.Confirm("Xóa ảnh", message, rows.Count == 1 ? "Xóa ảnh" : $"Xóa {rows.Count} ảnh", danger: true)) return;

        try
        {
            await RemoveRowsAsync(rows);
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            ErrorMessage = $"Không xóa được ảnh: {exception.Message}";
        }
    }

    private async Task RemoveRowsAsync(IReadOnlyList<ImageRowViewModel> rows)
    {
        var service = _service!;
        var editor = Editor!;
        var ids = rows.Select(row => row.Id).ToHashSet();
        var removingCurrent = _current is not null && ids.Contains(_current.Id);

        // The next image to show is worked out before the list changes: the one after, else the one before, else any.
        ImageRowViewModel? next = null;
        if (removingCurrent)
        {
            var anchor = _current!.Id;
            var shown = Matching().Where(row => !ids.Contains(row.Id)).ToList();
            next = shown.FirstOrDefault(row => row.Id > anchor)
                ?? shown.LastOrDefault(row => row.Id < anchor)
                ?? _allImages.FirstOrDefault(row => !ids.Contains(row.Id));
        }

        editor.Cancel();
        service.DeleteImages(ids.ToList());

        _lastDeleted = ids.ToList();
        _stripSelection = Array.Empty<ImageRowViewModel>();
        _allImages = _allImages.Where(row => !ids.Contains(row.Id)).ToList();
        HasImages = _allImages.Count > 0;
        RefreshDeletedCount();

        if (removingCurrent)
        {
            _current = null;
            if (next is not null)
            {
                await OpenImageAsync(next);
            }
            else
            {
                _loadVersion++;
                CurrentBitmap = null;
                editor.Open(null);
                Magic.SetImage(null);
                Labels.Clear();
                ImageInfoText = string.Empty;
            }
        }

        DeleteMessage = rows.Count == 1 ? $"Đã xóa {rows[0].FileName}." : $"Đã xóa {rows.Count} ảnh.";
        ShowImageList();
        UpdateSelectionText();
        UpdateProjectProgress();
        NotifyVisibility();
        DeleteImagesCommand.NotifyCanExecuteChanged();
        AutoLabelAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task UndoDeleteAsync()
    {
        if (_service is null || _lastDeleted.Count == 0) return;
        await RestoreAsync(_lastDeleted);
    }

    [RelayCommand]
    private async Task RestoreDeletedAsync()
    {
        if (_service is null) return;

        var deleted = _service.LoadDeletedIds();
        if (deleted.Count == 0) return;
        if (!_prompt.Confirm("Khôi phục ảnh", $"Đưa {deleted.Count} ảnh đã xóa trở lại project, với trạng thái và nhãn như trước khi xóa?", "Khôi phục")) return;

        await RestoreAsync(deleted);
    }

    private async Task RestoreAsync(IReadOnlyList<long> ids)
    {
        try
        {
            _service!.RestoreImages(ids);
            _lastDeleted = Array.Empty<long>();
            DeleteMessage = null;
            await RefreshAfterClassChangeAsync(reloadImages: true);
            HasImages = _allImages.Count > 0;
            RefreshDeletedCount();
            UpdateSelectionText();
            NotifyVisibility();
            AutoLabelAllCommand.NotifyCanExecuteChanged();
            ErrorMessage = null;
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            ErrorMessage = $"Không khôi phục được ảnh: {exception.Message}";
        }
    }
}
