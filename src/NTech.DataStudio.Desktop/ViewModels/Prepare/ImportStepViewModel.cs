using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;
using NTech.DataStudio.Desktop.Services;

namespace NTech.DataStudio.Desktop.ViewModels.Prepare;

public sealed partial class ImportStepViewModel : PrepareStepViewModel
{
    private readonly ImportService _import;
    private readonly IFolderPicker _folderPicker;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _folder = string.Empty;

    [ObservableProperty]
    private Option<ImportMode> _selectedMode;

    [ObservableProperty]
    private int _sourceCount;

    public ImportStepViewModel(ProjectService projects, ImportService import, IFolderPicker folderPicker)
        : base(projects)
    {
        _import = import;
        _folderPicker = folderPicker;
        _selectedMode = Modes[0];
    }

    public IReadOnlyList<Option<ImportMode>> Modes { get; } = new[]
    {
        new Option<ImportMode>("Chép ảnh vào project (chép sang máy khác vẫn mở được)", ImportMode.CopyIntoProject),
        new Option<ImportMode>("Chỉ tham chiếu đường dẫn (không tốn thêm ổ đĩa)", ImportMode.ReferenceInPlace)
    };

    public override Task ReloadAsync() => RunAsync("Đang đọc project", (store, _, _) =>
    {
        var count = store.LoadSources().Count;
        return () => SourceCount = count;
    });

    public void UseDroppedFolder(string folder) => Folder = folder;

    [RelayCommand]
    private void Browse()
    {
        var folder = _folderPicker.Pick("Chọn thư mục ảnh", string.IsNullOrWhiteSpace(Folder) ? null : Folder);
        if (folder is not null)
            Folder = folder;
    }

    private bool CanImport() => !string.IsNullOrWhiteSpace(Folder);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var folder = Folder;
        var mode = SelectedMode.Value;
        await RunAsync("Đang nhập ảnh", (store, progress, token) =>
        {
            if (!Directory.Exists(folder))
                throw new ProjectException($"Không tìm thấy thư mục \"{folder}\".");

            var result = _import.Import(store, folder, mode, progress, token);
            var count = store.LoadSources().Count;
            return () =>
            {
                SourceCount = count;
                ResultMessage = result.Added == 0 && result.AlreadyImported == 0
                    ? "Không tìm thấy ảnh BMP, TIFF, PNG, JPEG hay WebP nào trong thư mục này."
                    : $"Đã thêm {result.Added} ảnh, bỏ qua {result.AlreadyImported} ảnh đã có, {result.Corrupt} file hỏng.";
                if (result.Failures.Count > 0)
                    ErrorMessage = PrepareText.Failures(result.Failures);
            };
        });
    }
}
