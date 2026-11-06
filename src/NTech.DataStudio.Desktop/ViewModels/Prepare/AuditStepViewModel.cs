using CommunityToolkit.Mvvm.ComponentModel;
using NTech.DataStudio.Application.Prepare;
using NTech.DataStudio.Application.Projects;

namespace NTech.DataStudio.Desktop.ViewModels.Prepare;

public sealed partial class AuditStepViewModel : PrepareStepViewModel
{
    private const int ResolutionsShown = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImages))]
    [NotifyPropertyChangedFor(nameof(TotalSizeText))]
    [NotifyPropertyChangedFor(nameof(EstimatedSizeText))]
    [NotifyPropertyChangedFor(nameof(LargestFileText))]
    [NotifyPropertyChangedFor(nameof(TopResolutions))]
    private AuditReport? _report;

    public AuditStepViewModel(ProjectService projects)
        : base(projects)
    {
    }

    public bool HasImages => Report is { TotalFiles: > 0 };
    public string TotalSizeText => PrepareText.Bytes(Report?.TotalBytes ?? 0);
    public string EstimatedSizeText => "≈ " + PrepareText.Bytes(Report?.EstimatedMasterBytes ?? 0);
    public string LargestFileText => PrepareText.Bytes(Report?.LargestFileBytes ?? 0);
    public IEnumerable<AuditBucket> TopResolutions => Report?.Resolutions.Take(ResolutionsShown) ?? Enumerable.Empty<AuditBucket>();

    public override Task ReloadAsync() => RunAsync("Đang tổng hợp", (store, _, _) =>
    {
        var report = AuditReportBuilder.Build(store.LoadSources());
        return () => Report = report;
    });
}
